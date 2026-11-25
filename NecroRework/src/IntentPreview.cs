using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;
using NecroRework.Powers;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 在怪物攻击意图的数字后面追加「（x）」：
/// x = 玩家当前的格挡抵扣后、再经过奥斯提减伤（50% 分摊或「吸引仇恨」的全额吸收），
/// 玩家在这一手攻击里实际会掉的血。
///
/// 群怪时按敌人行动顺序（CombatState.Enemies 的顺序）做一次整体模拟：
/// 格挡在模拟中被逐个敌人消耗，所以后面那个敌人显示的是「前面的敌人打完之后」的剩余情况。
/// </summary>
internal static class IntentPreview
{
	// #C91515，不透明度 90%（E6）；字号 22 = 原来的 18 × 1.2
	private const string ColorOpen = "[color=#c91515e6][font_size=22]";
	private const string ColorClose = "[/font_size][/color]";

	/// <summary>「死者苏生」护佑期间的提示色：青色，用来表示这一手打不到人。</summary>
	private const string SafeColorOpen = "[color=#00d9ffe6][font_size=22]";
	private const string SafeColorClose = "[/font_size][/color]";

	/// <summary>每张「飘荡」牌在回合结束自动消耗时给的格挡。</summary>
	private const int DriftBlock = 4;

	private static readonly AccessTools.FieldRef<NIntent, MegaRichTextLabel> ValueLabelRef =
		AccessTools.FieldRefAccess<NIntent, MegaRichTextLabel>("_valueLabel");

	private static readonly AccessTools.FieldRef<NIntent, AbstractIntent> IntentRef =
		AccessTools.FieldRefAccess<NIntent, AbstractIntent>("_intent");

	private static readonly AccessTools.FieldRef<NIntent, Creature> OwnerRef =
		AccessTools.FieldRefAccess<NIntent, Creature>("_owner");

	private static readonly ConditionalWeakTable<NIntent, State> States = new();

	/// <summary>显示节点 → 它当前正在显示的意图（用于把模拟结果写回每一个节点）。</summary>
	private static readonly Dictionary<NIntent, AbstractIntent> Nodes = new();

	/// <summary>本轮模拟算出的「每个意图对应的（x）」——只存数字，颜色另记。</summary>
	private static readonly Dictionary<AbstractIntent, string> Suffixes = new();

	/// <summary>这一批里哪些意图的数字要用青色（死者苏生期间显示 0）。</summary>
	private static readonly HashSet<AbstractIntent> SafeSuffixes = new();

	/// <summary>意图节点 → 我们自己加的「伤害预判」小标签（贴在意图图标上方，避免和旁边的复合意图打架）。</summary>
	private static readonly Dictionary<NIntent, Label> ExtraLabels = new();

	private static readonly Color PreviewRed = new(0.788f, 0.082f, 0.082f, 1f);
	private static readonly Color PreviewCyan = new(0f, 0.851f, 1f, 1f);

	private sealed class State
	{
		public string BaseText = string.Empty;
	}

	public static void Install(Harmony harmony)
	{
		try
		{
			var visuals = AccessTools.Method(typeof(NIntent), "UpdateVisuals");
			if (visuals != null)
			{
				harmony.Patch(visuals, postfix: new HarmonyMethod(typeof(IntentPreview), nameof(UpdateVisualsPostfix)));
			}

			var process = AccessTools.Method(typeof(NIntent), "_Process", new[] { typeof(double) });
			if (process != null)
			{
				harmony.Patch(process, postfix: new HarmonyMethod(typeof(IntentPreview), nameof(ProcessPostfix)));
			}

			Main.Log.Info("意图伤害提示：已挂载（UpdateVisuals / _Process）");
		}
		catch (Exception ex)
		{
			Main.Log.Info("意图伤害提示挂载失败：" + ex);
		}
	}

	private static void UpdateVisualsPostfix(NIntent __instance)
	{
		try
		{
			State state = States.GetOrCreateValue(__instance);
			state.BaseText = ValueLabelRef(__instance).Text ?? string.Empty;

			AbstractIntent intent = IntentRef(__instance);
			if (intent != null)
			{
				Nodes[__instance] = intent;
			}

			Recompute(__instance);
		}
		catch (Exception ex)
		{
			Main.Log.Info("意图提示（UpdateVisuals）异常：" + ex.Message);
		}
	}

	private static void ProcessPostfix(NIntent __instance)
	{
		try
		{
			// 格挡/血量每帧都可能变，但没必要每帧重算整个战场：80ms 一次就够跟手了。
			if (Time.GetTicksMsec() - _lastComputeMsec < 80)
			{
				return;
			}

			Recompute(__instance);
		}
		catch (Exception ex)
		{
			Main.Log.Info("意图提示（_Process）异常：" + ex.Message);
		}
	}

	private static ulong _lastComputeMsec;

	/// <summary>按敌人行动顺序整体模拟一遍，然后把结果写回所有意图节点。</summary>
	private static void Recompute(NIntent source)
	{
	    if (!NecroSettings.IntentPreview) return;

		// 玩家点了结束回合之后：不再重算，只把上一轮算好的数字贴回去，避免敌人回合里数字乱跳
		if (FreezeGate.IsFrozen())
		{
			ApplyAll();
			return;
		}

		_lastComputeMsec = Time.GetTicksMsec();

		Creature owner = OwnerRef(source);
		var combatState = owner?.CombatState;
		if (combatState == null)
		{
			return;
		}

		// 联机时每个玩家看到的预判必须按「自己」算：用 LocalContext 取本机玩家，
		// 拿不到（单人局 / 还没拿到 NetId）才回落到列表里的第一个。
		Creature player = LocalContext.GetMe(combatState)?.Creature
			?? combatState.Players.FirstOrDefault()?.Creature;
		if (player == null)
		{
			return;
		}

		// 亡魂牵引会拦下的牌（回合结束自动打出），它们既不进消耗堆、也不再触发飘荡的起防
		List<CardModel> autoPlayed = CollectAutoPlayedCards(player);

		int block = player.Block + ProjectedEndOfTurnBlock(player, autoPlayed) - ProjectedEndOfTurnSelfDamage(player);
		if (block < 0)
		{
			block = 0;
		}

		// 这些牌在敌人动手之前就会被打出：先算它们的格挡/自伤/敌人触发
		AutoPlayForecast forecast = ForecastAutoPlays(player, autoPlayed, combatState);
		block += forecast.Block;
		int preDamage = Math.Max(0, forecast.SelfDamage - block);
		block = Math.Max(0, block - forecast.SelfDamage);
		Creature osty = player.Player?.Osty;
		int ostyHp = osty is { IsAlive: true } ? osty.CurrentHp : 0;

		// 灾厄 >= 当前生命值的奥斯提会在本回合结束时被灾厄带走，敌人打过来时它已经没了，
		// 所以这里直接当作「没有奥斯提」来算。
		if (ostyHp > 0 && osty != null && DoomUtil.DoomOf(osty) >= ostyHp)
		{
			ostyHp = 0;
		}

		bool guard = ostyHp > 0 && osty.HasPower<OstyGuardPower>();

		// 死者苏生的护佑还在：这一段敌人回合里，骨妹和奥斯提都不会掉血（也不会死于灾厄），
		// 所以所有攻击意图都显示青色的 0。
		bool undying = player.HasPower<ReanimateWardPower>();

		// 瓦皮（Wuppy）：「瓦皮的忠诚」在扣格挡之前就把攻击目标改成瓦皮自己，
		// 所以它活着的每一段攻击，玩家既不掉血、也不消耗格挡；瓦皮被打死之后，
		// 后面的段数（和后面的敌人）才回到「格挡 → 奥斯提」的老规则。
		Creature wuppy = player.Player?.PlayerCombatState?.GetPet<Wuppy>();
		bool wuppyGuarding = wuppy is { IsAlive: true } && wuppy.HasPower<WuppysLoyaltyPower>();
		int wuppyBlock = wuppyGuarding ? wuppy!.Block : 0;
		int wuppyHp = wuppyGuarding ? wuppy!.CurrentHp : 0;

		var targets = combatState.Players.Select(p => p.Creature).ToList();

		// 「吸引仇恨 + 多段攻击 + 厄运之衣」的连锁：
		//   奥斯提全吸收 → 它掉血 → 「殃及」每 2 点掉血给你 1 层灾厄 → 「厄运之衣」每层灾厄给你格挡。
		// 这几块格挡会滚进后面的每一段攻击，所以要在整段敌人回合里一直累计。
		int shroudBlock = player.GetPower<ShroudPower>()?.Amount ?? 0;
		bool spillover = player.HasPower<SpilloverPower>();
		int spillBuffer = 0;

		Suffixes.Clear();
		SafeSuffixes.Clear();

		foreach (Creature enemy in combatState.Enemies)
		{
			IReadOnlyList<AbstractIntent> intents = enemy.Monster?.NextMove?.Intents;
			if (intents == null)
			{
				continue;
			}

			foreach (AbstractIntent intent in intents)
			{
				if (intent is not AttackIntent attack)
				{
					continue;
				}

				int single = attack.GetSingleDamage(targets, enemy);

				// 激怒：自动打出的技能牌会让这名敌人获得力量，之后的每一下都更疼
				int enrage = (enemy.GetPower<EnragePower>()?.Amount ?? 0) * forecast.SkillCount;
				single += enrage;

				if (single <= 0)
				{
					continue;
				}

				if (undying)
				{
					Suffixes[intent] = "0";
					SafeSuffixes.Add(intent);
					continue;
				}

				int repeats = attack.Repeats > 0 ? attack.Repeats : 1;
				int taken = 0;

				for (int i = 0; i < repeats; i++)
				{
					int damage = single;

					// 瓦皮先吃：它的格挡、再它的血；瓦皮被打死时，溢出的伤害会无缝接到玩家身上
					if (wuppyGuarding)
					{
						int absorbed = Math.Min(wuppyBlock, damage);
						wuppyBlock -= absorbed;
						damage -= absorbed;

						int hpLoss = Math.Min(wuppyHp, damage);
						wuppyHp -= hpLoss;
						damage -= hpLoss;

						if (wuppyHp <= 0)
						{
							wuppyGuarding = false;
						}
					}

					if (damage <= 0)
					{
						continue;
					}

					int unblocked = Math.Max(0, damage - block);
					block = Math.Max(0, block - damage);
					if (unblocked <= 0)
					{
						continue;
					}

					if (ostyHp > 0)
					{
						int share = guard ? unblocked : (int)Math.Ceiling(unblocked / 2m);
						share = Math.Min(share, ostyHp);
						ostyHp -= share;
						taken += unblocked - share;

						// 奥斯提掉血 → 「殃及」给玩家灾厄 → 「厄运之衣」转成格挡（每 2 点掉血 1 层灾厄）
						if (share > 0 && spillover && shroudBlock > 0)
						{
							spillBuffer += share;
							int doomGained = spillBuffer / 2;
							if (doomGained > 0)
							{
								spillBuffer -= doomGained * 2;
								block += doomGained * shroudBlock;
							}
						}

						// 奥斯提在这一段被打死：「吸引仇恨」随之消失，后面的段数不再由它承担
						if (ostyHp <= 0)
						{
							guard = false;
						}
					}
					else
					{
						taken += unblocked;
					}
				}

				if (preDamage > 0 && taken >= 0)
				{
					// 格挡不够吃掉的那部分自伤（死亡律动/流电），算进第一段攻击的数字里
					taken += preDamage;
					preDamage = 0;
				}

				Suffixes[intent] = taken > 0 ? taken.ToString() : string.Empty;
			}
		}

		ApplyAll();
	}

	/// <summary>
	/// 回合结束时、敌人动手之前还会自动加到身上的格挡。按引擎的结算顺序覆盖：
	///  1) 奥利哈刚 / 伪奥利哈刚（BeforeSideTurnEndVeryEarly）：这一瞬间格挡为 0 才触发，
	///     所以用 player.Block 的原始值判断（覆甲、披风扣这些都还没结算）；
	///  2) 覆甲（PlatingPower，BeforeSideTurnEndEarly）：按层数获得格挡；
	///  3) 披风扣（CloakClasp）：手牌张数 × 每张 1 点；
	///  4) 涟漪盆（RippleBasin）：本回合没打出过攻击牌时 +4；
	///  5) 手里的「飘荡」牌：它们带虚无，回合结束会自动消耗，每张给 4 点格挡。
	/// 全部是 ValueProp.Unpowered，不吃敏捷/脆弱，直接加整数。
	/// </summary>
	private static int ProjectedEndOfTurnBlock(Creature player, List<CardModel> autoPlayed)
	{
		var me = player.Player;
		if (me == null)
		{
			return 0;
		}

		int projected = 0;

		// 1) 奥利哈刚一族：引擎看的是「这一瞬间格挡是否为 0」，此时其它落甲都还没结算。
		if (player.Block <= 0)
		{
			foreach (RelicModel relic in me.Relics)
			{
				if (relic is MegaCrit.Sts2.Core.Models.Relics.Orichalcum or MegaCrit.Sts2.Core.Models.Relics.FakeOrichalcum)
				{
					projected += (int)relic.DynamicVars.Block.BaseValue;
				}
			}
		}

		// 2) 覆甲
		PlatingPower plating = player.GetPower<PlatingPower>();
		if (plating != null && plating.Amount > 0)
		{
			projected += plating.Amount;
		}

		CardPile hand = PileType.Hand.GetPile(me);

		foreach (RelicModel relic in me.Relics)
		{
			// 3) 披风扣：手牌张数 × 每张点数
			if (relic is MegaCrit.Sts2.Core.Models.Relics.CloakClasp && hand != null && hand.Cards.Count > 0)
			{
				projected += (int)(hand.Cards.Count * relic.DynamicVars.Block.BaseValue);
			}

			// 4) 涟漪盆：本回合没打出过攻击牌才触发
			if (relic is MegaCrit.Sts2.Core.Models.Relics.RippleBasin && !PlayedAttackThisTurn(me))
			{
				projected += (int)relic.DynamicVars.Block.BaseValue;
			}
		}

		// 5) 飘荡牌
		if (hand != null)
		{
			foreach (CardModel card in hand.Cards)
			{
				if (card.Enchantment is DriftEnchantment
					&& card.Keywords.Contains(CardKeyword.Ethereal)
					&& !autoPlayed.Contains(card))
				{
					projected += DriftBlock;
				}
			}
		}

		return projected;
	}

	/// <summary>本回合有没有打出过攻击牌（涟漪盆的触发条件）。取不到战斗状态时按「打过了」处理，宁可少算格挡。</summary>
	private static bool PlayedAttackThisTurn(Player player)
	{
		try
		{
			ICombatState? combatState = player.Creature?.CombatState;
			if (combatState == null)
			{
				return true;
			}

			return CombatManager.Instance.History.CardPlaysFinished.Any(e =>
				e.HappenedThisTurn(combatState)
				&& e.CardPlay.Card.Type == CardType.Attack
				&& e.CardPlay.Player == player);
		}
		catch
		{
			return true;
		}
	}

	/// <summary>亡魂牵引会拦下的牌：手牌从左到右前 N 张带「虚无」的牌（N = 层数）。</summary>
	private static List<CardModel> CollectAutoPlayedCards(Creature player)
	{
		var result = new List<CardModel>();
		var me = player.Player;
		if (me == null)
		{
			return result;
		}

		int stacks = player.GetPower<PullFromBelowPower>()?.Amount ?? 0;
		if (stacks <= 0)
		{
			return result;
		}

		CardPile hand = PileType.Hand.GetPile(me);
		if (hand == null)
		{
			return result;
		}

		foreach (CardModel card in hand.Cards)
		{
			if (result.Count >= stacks)
			{
				break;
			}

			if (card.Keywords.Contains(CardKeyword.Ethereal))
			{
				result.Add(card);
			}
		}

		return result;
	}

	private sealed class AutoPlayForecast
	{
		public int Block;
		public int SelfDamage;
		public int SkillCount;
	}

	/// <summary>
	/// 预演「亡魂牵引自动打出的那几张牌」在敌人动手前会发生什么：
	///  1) 牌面自带的格挡（脆弱会砍 25%，向下取整）；
	///  2) 每打出一张就吃一次死亡律动的伤害；
	///  3) 能力牌身上有「流电」侵蚀时，打出会挨一份流电伤害；
	///  4) 技能牌会喂敌人的「激怒」（每张 +Amount 力量，影响该敌人之后每一段攻击）。
	/// </summary>
	private static AutoPlayForecast ForecastAutoPlays(Creature player, List<CardModel> cards, ICombatState combatState)
	{
		var forecast = new AutoPlayForecast();
		if (cards.Count == 0)
		{
			return forecast;
		}

		bool frail = player.HasPower<FrailPower>();
		int deathRhythm = player.GetPower<DeathRhythmPower>()?.Amount ?? 0;

		int galvanic = 0;
		foreach (Creature enemy in combatState.Enemies)
		{
			GalvanicPower? power = enemy.GetPower<GalvanicPower>();
			if (power != null && power.Amount > galvanic)
			{
				galvanic = power.Amount;
			}
		}

		foreach (CardModel card in cards)
		{
			if (card.Type == CardType.Skill)
			{
				forecast.SkillCount++;
			}

			if (card.DynamicVars.TryGetValue("Block", out DynamicVar? blockVar))
			{
				decimal value = blockVar.BaseValue;
				if (frail)
				{
					value *= 0.75m;
				}

				forecast.Block += (int)Math.Floor(value);
			}

			if (deathRhythm > 0)
			{
				forecast.SelfDamage += deathRhythm;
			}

			if (galvanic > 0 && card.Type == CardType.Power && card.Affliction is Galvanized)
			{
				forecast.SelfDamage += galvanic;
			}
		}

		return forecast;
	}

	/// <summary>
	/// 回合结束时、敌人动手之前先打到玩家身上、并且会吃掉格挡的伤害。
	/// 覆盖两类：
	///  1) 手牌里「回合结束时造成伤害」的状态牌（凋萎/灼伤/腐朽/感染/剧毒等），
	///     只要它的伤害不是「无法格挡」就计入（债务/疑虑/羞愧不吃血，运气/召唤/悔恨是无法格挡，都不会被算进来）；
	///  2) 挂在玩家身上、按自身层数打玩家且可被格挡的负面能力（收缩/崩解/魔法炸弹/无厌沙虫的支配）。
	/// </summary>
	private static int ProjectedEndOfTurnSelfDamage(Creature player)
	{
		var me = player.Player;
		if (me == null)
		{
			return 0;
		}

		int damage = 0;

		CardPile hand = PileType.Hand.GetPile(me);
		if (hand != null)
		{
			foreach (CardModel card in hand.Cards)
			{
				if (!card.HasTurnEndInHandEffect)
				{
					continue;
				}

				if (!card.DynamicVars.TryGetValue("Damage", out DynamicVar dyn) || dyn is not DamageVar damageVar)
				{
					continue;
				}

				if ((damageVar.Props & ValueProp.Unblockable) != 0)
				{
					continue;
				}

				damage += (int)Math.Ceiling(damageVar.BaseValue);
			}
		}

		if (player.GetPower<ConstrictPower>() is { } constrict)
		{
			damage += constrict.Amount;
		}

		if (player.GetPower<DisintegrationPower>() is { } disintegration)
		{
			damage += disintegration.Amount;
		}

		if (player.GetPower<MagicBombPower>() is { } magicBomb)
		{
			damage += magicBomb.Amount;
		}

		if (player.GetPower<InsatiableDominionPower>() is { } dominion
			&& dominion.DynamicVars.TryGetValue("Damage", out DynamicVar dominionDamage))
		{
			damage += (int)Math.Ceiling(dominionDamage.BaseValue);
		}

		return damage;
	}

	/// <summary>把这一轮的模拟结果写到所有还活着的意图节点上。</summary>
	private static void ApplyAll()
	{
		List<NIntent> dead = null;

		foreach (KeyValuePair<NIntent, AbstractIntent> pair in Nodes)
		{
			NIntent node = pair.Key;
			if (!GodotObject.IsInstanceValid(node))
			{
				(dead ??= new List<NIntent>()).Add(node);
				continue;
			}

			if (!States.TryGetValue(node, out State state))
			{
				continue;
			}

			string suffix = Suffixes.TryGetValue(pair.Value, out string text) ? text : string.Empty;
			bool safe = SafeSuffixes.Contains(pair.Value);
			MegaRichTextLabel label = ValueLabelRef(node);
			bool solo = IsSoloIntent(node);

			if (label != null)
			{
				// 敌人只有这一个攻击意图时，保持原版位置与字号：直接把「（x）」接在数字后面。
				// 复合意图（攻击 + 别的图标并排）才挪到图标上方，否则会压住右边的图标。
				if (solo && suffix.Length > 0)
				{
					string open = safe ? "[color=#00d9ffe6][font_size=22]" : "[color=#c91515e6][font_size=22]";
					label.Text = state.BaseText + open + "（" + suffix + "）[/font_size][/color]";
				}
				else
				{
					label.Text = state.BaseText;
				}
			}

			UpdateExtraLabel(node, solo ? string.Empty : suffix, safe);
		}

		if (dead != null)
		{
			foreach (NIntent node in dead)
			{
				Nodes.Remove(node);
				ExtraLabels.Remove(node);
			}
		}
	}

	/// <summary>这个意图是不是敌人身上唯一的一个（只有攻击意图的敌人才走原版位置）。</summary>
	private static bool IsSoloIntent(NIntent node)
	{
		try
		{
			// 首选：直接看这只怪这一步到底有几个意图。
			// 不能只看父节点里的子节点数量 —— 多人局里同一个容器可能同时装着别的敌人/别的玩家的意图，
			// 那样「单独攻击的敌人」也会被算成复合意图（表现就是数字跑到上方且变小）。
			Creature? owner = IntentOwner(node);
			if (owner?.Monster?.NextMove?.Intents is { } intents)
			{
				return intents.Count <= 1;
			}
		}
		catch
		{
			// 反射失败就退回下面的可见子节点计数
		}

		try
		{
			Node? parent = node.GetParent();
			if (parent == null) return true;

			int count = 0;
			foreach (Node child in parent.GetChildren())
			{
				if (child is NIntent candidate && GodotObject.IsInstanceValid(candidate) && candidate.Visible)
				{
					count++;
					if (count > 1) return false;
				}
			}

			return count <= 1;
		}
		catch
		{
			return true;
		}
	}

	/// <summary>取意图节点对应的生物（NIntent 的私有字段 _owner）。</summary>
	private static Creature? IntentOwner(NIntent node)
	{
		try
		{
			return OwnerRef(node);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>把「预判伤害」写到意图节点上方的小标签里；没有数字就隐藏。</summary>
	private static void UpdateExtraLabel(NIntent node, string suffix, bool safe)
	{
		if (string.IsNullOrEmpty(suffix))
		{
			if (ExtraLabels.TryGetValue(node, out Label hidden) && GodotObject.IsInstanceValid(hidden))
			{
				hidden.Visible = false;
			}

			return;
		}

		Label? shown = null;
		if (ExtraLabels.TryGetValue(node, out Label cached) && GodotObject.IsInstanceValid(cached))
		{
			shown = cached;
		}

		if (shown == null)
		{
			shown = new Label
			{
				Name = "NecroPreviewValue",
				Text = string.Empty,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			node.AddChild(shown);
			ExtraLabels[node] = shown;
		}

		// 复合意图时数字挪到图标上方：字号取原版意图数字（22）的 90% ≈ 20，再按需求放大 1.3 倍 → 26；
		// 标签宽度/高度同比放大，x 用「宽的一半」抵消，保证水平仍居中在 64 宽的图标上。
		shown.Position = new Vector2(-20f, -33f);
		shown.Size = new Vector2(104f, 26f);
		shown.AddThemeFontSizeOverride("font_size", 26);
		shown.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.6f));
		shown.AddThemeConstantOverride("outline_size", 9);
		shown.Text = "（" + suffix + "）";
		shown.AddThemeColorOverride("font_color", safe ? PreviewCyan : PreviewRed);
		shown.Visible = true;
	}
}
