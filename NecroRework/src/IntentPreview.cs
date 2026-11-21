using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;
using NecroRework.Powers;

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

	/// <summary>本轮模拟算出的「每个意图对应的（x）」。</summary>
	private static readonly Dictionary<AbstractIntent, string> Suffixes = new();

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
		_lastComputeMsec = Time.GetTicksMsec();

		Creature owner = OwnerRef(source);
		var combatState = owner?.CombatState;
		if (combatState == null)
		{
			return;
		}

		Creature player = combatState.Players.FirstOrDefault()?.Creature;
		if (player == null)
		{
			return;
		}

		int block = player.Block + ProjectedEndOfTurnBlock(player) - ProjectedEndOfTurnSelfDamage(player);
		if (block < 0)
		{
			block = 0;
		}
		Creature osty = player.Player?.Osty;
		int ostyHp = osty is { IsAlive: true } ? osty.CurrentHp : 0;

		// 灾厄 >= 当前生命值的奥斯提会在本回合结束时被灾厄带走，敌人打过来时它已经没了，
		// 所以这里直接当作「没有奥斯提」来算。
		if (ostyHp > 0 && osty != null && DoomUtil.DoomOf(osty) >= ostyHp)
		{
			ostyHp = 0;
		}

		bool guard = ostyHp > 0 && osty.HasPower<OstyGuardPower>();
		var targets = combatState.Players.Select(p => p.Creature).ToList();

		Suffixes.Clear();

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
				if (single <= 0)
				{
					continue;
				}

				int repeats = attack.Repeats > 0 ? attack.Repeats : 1;
				int taken = 0;

				for (int i = 0; i < repeats; i++)
				{
					int unblocked = Math.Max(0, single - block);
					block = Math.Max(0, block - single);
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
					}
					else
					{
						taken += unblocked;
					}
				}

				Suffixes[intent] = taken > 0 ? ColorOpen + "（" + taken + "）" + ColorClose : string.Empty;
			}
		}

		ApplyAll();
	}

	/// <summary>
	/// 回合结束时、敌人动手之前还会自动加到身上的格挡。
	/// 目前覆盖两种：
	///  1) 覆甲（PlatingPower）：回合结束时按层数获得格挡；
	///  2) 手里的「飘荡」牌：它们带虚无，回合结束会自动消耗，每张给 4 点格挡。
	/// </summary>
	private static int ProjectedEndOfTurnBlock(Creature player)
	{
		var me = player.Player;
		if (me == null)
		{
			return 0;
		}

		int projected = 0;

		PlatingPower plating = player.GetPower<PlatingPower>();
		if (plating != null && plating.Amount > 0)
		{
			projected += plating.Amount;
		}

		CardPile hand = PileType.Hand.GetPile(me);
		if (hand != null)
		{
			foreach (CardModel card in hand.Cards)
			{
				if (card.Enchantment is DriftEnchantment && card.Keywords.Contains(CardKeyword.Ethereal))
				{
					projected += DriftBlock;
				}
			}
		}

		return projected;
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
			MegaRichTextLabel label = ValueLabelRef(node);
			if (label != null)
			{
				label.Text = state.BaseText + suffix;
			}
		}

		if (dead != null)
		{
			foreach (NIntent node in dead)
			{
				Nodes.Remove(node);
			}
		}
	}
}
