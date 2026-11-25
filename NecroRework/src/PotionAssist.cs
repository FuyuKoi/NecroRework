using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using NecroRework.Settings;

namespace NecroRework;

/// <summary>
/// 「援助奥斯提」：力量类药水（CanonicalVars 里有 StrengthPower 的那种，例如力量药水 / 灵活药水 / 菲什油）
/// 拿起后会进入选目标状态，除了队友还能点奥斯提，把力量灌给奥斯提。
///
/// 原版之所以点不到奥斯提，有三道门槛，三处都要放宽：
///  1) <see cref="PotionModel.CanThrowAtAlly"/> 为假时，拿起药水直接对自己生效，根本不进入选目标；
///  2) <see cref="PotionModel.IsValidTarget"/> 里 AnyPlayer 只认 <c>target.IsPlayer</c>，奥斯提不是玩家；
///  3) <c>NTargetManager.AllowedToTargetCreature</c> 里 AnyPlayer 同样只认玩家，鼠标连悬停都不允许。
/// </summary>
internal static class PotionAssist
{
	/// <summary>
	/// 认药水看它的变量里有没有这几个能力键（而不是按药水类名硬编码，这样以后新增的同类药水自动生效）：
	///  - StrengthPower：力量药水 / 灵活药水 / 菲什油
	///  - RitualPower：仪式药水（玛扎莱特的赠礼，每回合开始加力量，对奥斯提同样有效）
	/// </summary>
	private static readonly string[] OstyBuffPowerKeys = ["StrengthPower", "RitualPower"];

	private static readonly AccessTools.FieldRef<HoveredModelTracker, PotionModel> SelectedPotionRef =
		AccessTools.FieldRefAccess<HoveredModelTracker, PotionModel>("_localSelectedPotion");

	internal static void Install(Harmony harmony)
	{
		MethodInfo canThrow = AccessTools.Method(typeof(PotionModel), nameof(PotionModel.CanThrowAtAlly));
		MethodInfo isValid = AccessTools.Method(typeof(PotionModel), nameof(PotionModel.IsValidTarget));
		MethodInfo allowed = AccessTools.Method(typeof(NTargetManager), "AllowedToTargetCreature");

		if (canThrow != null)
		{
			harmony.Patch(canThrow, postfix: new HarmonyMethod(typeof(PotionAssist), nameof(CanThrowAtAllyPostfix)));
		}

		if (isValid != null)
		{
			harmony.Patch(isValid, prefix: new HarmonyMethod(typeof(PotionAssist), nameof(IsValidTargetPrefix)));
		}

		if (allowed != null)
		{
			harmony.Patch(allowed, prefix: new HarmonyMethod(typeof(PotionAssist), nameof(AllowedToTargetCreaturePrefix)));
		}

		Main.Log.Info($"药水援助奥斯提已挂载（CanThrowAtAlly={canThrow != null} / IsValidTarget={isValid != null} / AllowedToTargetCreature={allowed != null}）");
	}

	/// <summary>当前被「拿起」的药水（只在选目标期间有值，靠它区分「药水选目标」和「卡牌选目标」）。</summary>
	private static PotionModel? SelectedPotion()
	{
		try
		{
			return SelectedPotionRef(RunManager.Instance.HoveredModelTracker);
		}
		catch
		{
			return null;
		}
	}

	private static bool IsStrengthPotion(PotionModel potion)
	{
		try
		{
			foreach (string key in OstyBuffPowerKeys)
			{
				if (potion.DynamicVars.ContainsKey(key))
				{
					return true;
				}
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 这瓶药水能不能喂给这个目标。
	/// 联机时放宽成「队伍里**任何一个人**的奥斯提」：
	/// 队友可以把自己的力量药水丢给骨妹的奥斯提（房主/客机都按本机视角各自判定）。
	/// </summary>
	private static bool ShouldAllow(PotionModel potion, Creature? target)
	{
		if (!NecroSettings.PotionAssist)
		{
			return false;
		}

		if (!IsAnyOsty(target))
		{
			return false;
		}

		if (!IsStrengthPotion(potion))
		{
			return false;
		}

		// 顺带确认是同一场战斗里的奥斯提，避免结算界面残留的节点被点到。
		return target!.CombatState == potion.Owner?.Creature?.CombatState;
	}

	/// <summary>这个生物是不是「队伍里某个人的奥斯提」，并且还活着。</summary>
	private static bool IsAnyOsty(Creature? target)
	{
		return target is { IsAlive: true, Monster: Osty } && target.PetOwner != null;
	}

	/// <summary>这场战斗里是否存在任何一只活着的奥斯提（决定要不要进入选目标状态）。</summary>
	private static bool HasAnyLivingOsty(PotionModel potion)
	{
		try
		{
			IReadOnlyList<Player>? players = potion.Owner?.Creature?.CombatState?.Players;
			if (players == null)
			{
				return false;
			}

			foreach (Player player in players)
			{
				if (player.Osty is { IsAlive: true })
				{
					return true;
				}
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	private static void CanThrowAtAllyPostfix(PotionModel __instance, ref bool __result)
	{
		if (__result || !NecroSettings.PotionAssist)
		{
			return;
		}

		if (!IsStrengthPotion(__instance))
		{
			return;
		}

		// 联机时自己不是骨妹、但队友的奥斯提在场上时，也要进入选目标，否则药水会直接灌给自己。
		if (!HasAnyLivingOsty(__instance))
		{
			return;
		}

		__result = true;
	}

	private static bool IsValidTargetPrefix(PotionModel __instance, Creature? target, ref bool __result)
	{
		if (target == null || !ShouldAllow(__instance, target))
		{
			return true;
		}

		__result = true;
		return false;
	}

	private static bool AllowedToTargetCreaturePrefix(Creature creature, ref bool __result)
	{
		PotionModel? potion = SelectedPotion();
		if (potion == null || !ShouldAllow(potion, creature))
		{
			return true;
		}

		__result = true;
		return false;
	}
}
