using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework;

/// <summary>
/// 让「灾厄」也能杀死奥斯提。
///
/// 原版 DoomPower 的结算只认两种参与者：
///   - 敌方回合结束时，把敌方阵营里灾厄 >= 当前生命值的怪处死；
///   - 玩家回合结束时，把玩家阵营里同样条件的生物处死。
/// 但玩家回合结束时传给 Hook 的 participants 只有「正在结束回合的玩家生物」，
/// 奥斯提（宠物）不在这份名单里，DoomPower.ShouldDoomTrigger 会因此直接放弃，
/// 导致奥斯提身上灾厄满了也永远不死。
///
/// 这里在 Hook.AfterSideTurnEnd 之后补一刀：玩家回合结束时，把玩家阵营里
/// 还活着、且灾厄 >= 当前生命值的生物（奥斯提 / 玩家自己）交给 DoomKill 处理。
/// 已经被原版处理掉的生物 IsAlive 为 false，DoomKill 内部也会跳过。
/// </summary>
internal static class OstyDoomDeath
{
	public static void Install(Harmony harmony)
	{
		try
		{
			var target = AccessTools.Method(typeof(Hook), "AfterSideTurnEnd", new[]
			{
				typeof(ICombatState),
				typeof(CombatSide),
				typeof(IEnumerable<Creature>)
			});

			if (target == null)
			{
				Main.Log.Info("灾厄处死奥斯提：没找到 Hook.AfterSideTurnEnd");
				return;
			}

			harmony.Patch(target, postfix: new HarmonyMethod(typeof(OstyDoomDeath), nameof(AfterSideTurnEndPostfix)));
			Main.Log.Info("灾厄处死奥斯提：已挂载");

			PatchOstySoItDoesNotDisappearFromDoom(harmony);
		}
		catch (Exception ex)
		{
			Main.Log.Info("灾厄处死奥斯提挂载失败：" + ex);
		}
	}

	/// <summary>
	/// 原版怪物被灾厄处死时默认会「消失」（尸体连同节点一起销毁），只有少数怪物改成了 false。
	/// 奥斯提属于宠物、死了要能被再次召唤，所以这里把它的 ShouldDisappearFromDoom 强制改成 false，
	/// 让灾厄处死走和普通死亡一样的流程：尸体留下、可以召唤复活。
	/// </summary>
	private static void PatchOstySoItDoesNotDisappearFromDoom(Harmony harmony)
	{
		try
		{
			var getter = AccessTools.PropertyGetter(typeof(MonsterModel), "ShouldDisappearFromDoom");

			if (getter == null)
			{
				Main.Log.Info("奥斯提灾厄身亡不消失：没找到 ShouldDisappearFromDoom");
				return;
			}

			harmony.Patch(getter, postfix: new HarmonyMethod(typeof(OstyDoomDeath), nameof(ShouldDisappearFromDoomPostfix)));
			Main.Log.Info("奥斯提灾厄身亡不消失：已挂载");
		}
		catch (Exception ex)
		{
			Main.Log.Info("奥斯提灾厄身亡不消失挂载失败：" + ex);
		}
	}

	private static void ShouldDisappearFromDoomPostfix(MonsterModel __instance, ref bool __result)
	{
		if (__instance is Osty)
		{
			__result = false;
		}
	}

	private static async Task AfterSideTurnEndPostfix(Task __result, ICombatState combatState, CombatSide side)
	{
		await __result;

		if (side != CombatSide.Player || combatState == null)
		{
			return;
		}

		if (CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		List<Creature> doomed = combatState.GetCreaturesOnSide(CombatSide.Player)
			.Where(c => c.IsAlive && c.CurrentHp <= DoomUtil.DoomOf(c))
			.Where(c => !Powers.ReanimateWardPower.IsWarded(c))
			.ToList();

		if (doomed.Count == 0)
		{
			return;
		}

		Main.Log.Info("灾厄处死：" + string.Join(", ", doomed.Select(c => $"{c.Name}({c.CurrentHp}HP/{DoomUtil.DoomOf(c)}灾厄)")));
		await DoomPower.DoomKill(doomed);
	}
}
