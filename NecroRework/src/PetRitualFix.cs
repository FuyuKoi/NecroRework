using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework;

/// <summary>
/// 宠物（奥斯提）吃不到「回合结束」类能力的修复。
///
/// 引擎里玩家回合结束那一刻传给钩子的名单是「所有结束回合的玩家」：
/// <c>Hook.AfterSideTurnEnd(turnState.State, CurrentSide, playersEndingTurn.Select(p =&gt; p.Creature))</c>
/// （decompiled\sts2_combat\MegaCrit_Sts2_Core_Combat_CombatManager.cs:1498），
/// 而绝大多数能力都用 <c>participants.Contains(Owner)</c> 判断「是不是轮到我」，
/// 于是挂在宠物身上的「仪式」（RitualPower）永远不结算，仪式层数涨了却不给力量。
///
/// 这里只补 仪式，判定条件是「这个能力确实挂在宠物身上、且原版名单里没有它」，
/// 原版该跑的场景一律 return true 放行，不会重复结算。
/// </summary>
internal static class PetRitualFix
{
    public static void Install(Harmony harmony)
    {
        MethodInfo? target = AccessTools.Method(typeof(RitualPower), nameof(RitualPower.AfterSideTurnEnd));
        if (target is null)
        {
            Main.Log.Warn($"[{Main.ModId}] 宠物仪式：找不到 RitualPower.AfterSideTurnEnd，跳过");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(PetRitualFix), nameof(AfterSideTurnEndPrefix))));

        Main.Log.Info($"[{Main.ModId}] 宠物仪式：已挂载（奥斯提身上的仪式现在会在自己这边回合结束时给力量）");
    }

    private static bool AfterSideTurnEndPrefix(
        RitualPower __instance,
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants,
        ref Task __result)
    {
        Creature? owner = __instance.Owner;
        if (owner is null || !owner.IsPet) return true;
        if (owner.Side != side) return true;
        if (participants.Contains(owner)) return true;   // 原版名单里有它 → 交给原版

        __result = ApplyAsync(__instance, choiceContext);
        return false;
    }

    private static async Task ApplyAsync(RitualPower power, PlayerChoiceContext choiceContext)
    {
        Creature owner = power.Owner;
        await PowerCmd.Apply<StrengthPower>(choiceContext, owner, power.Amount, owner, null);
        Main.Log.Info($"[{Main.ModId}] 仪式（宠物）：{owner.Name} 回合结束获得 {power.Amount} 点力量");
    }
}
