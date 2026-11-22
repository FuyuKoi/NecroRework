using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace NecroRework;

/// <summary>
/// 骨妹两个初始命匣的加强：
/// <list type="bullet">
/// <item><see cref="BoundPhylactery"/>（缚魂命匣）：前三个回合每回合开始时召唤 2，之后每回合召唤 1。</item>
/// <item><see cref="PhylacteryUnbound"/>（无界命匣）：每场战斗开始时召唤 8，之后每回合开始时召唤 3。</item>
/// </list>
/// 原版这两个遗物把数值写在 <c>CanonicalVars</c> 里、调用点固定，所以只能
/// ①改写数值变量（描述会跟着变）②按回合数接管召唤。
/// </summary>
internal static class StarterRelicRework
{
    /// <summary>前几个回合享受加强后的召唤量。</summary>
    private const int BoostedTurns = 3;

    private const decimal BoundBoostedSummon = 2m;
    private const decimal BoundNormalSummon = 1m;

    private const decimal UnboundCombatStartSummon = 8m;
    private const decimal UnboundTurnSummon = 3m;

    public static void Install(Harmony harmony)
    {
        // 缚魂命匣：描述改成写死的新规则（数值随回合变，没法用动态变量表达）
        PatchGetter(harmony, typeof(BoundPhylactery), "CanonicalVars", nameof(BoundVarsPrefix));
        Patch(harmony, typeof(BoundPhylactery), "BeforeCombatStart", nameof(BoundBeforeCombatStartPrefix));
        Patch(harmony, typeof(BoundPhylactery), "AfterEnergyResetLate", nameof(BoundAfterEnergyResetLatePrefix));

        // 无界命匣：数值变量改成 8 / 3，描述会自动跟着变
        PatchGetter(harmony, typeof(PhylacteryUnbound), "CanonicalVars", nameof(UnboundVarsPrefix));
        // 第一回合的 8 点由 BeforeCombatStart 给，回合开始那一发要跳掉，否则第一回合会变成 8+3
        Patch(harmony, typeof(PhylacteryUnbound), "AfterSideTurnStart", nameof(UnboundAfterSideTurnStartPrefix));
    }

    private static void Patch(Harmony harmony, Type type, string method, string prefixName)
    {
        MethodInfo? target = AccessTools.Method(type, method);
        if (target is null)
        {
            Main.Log.Warn($"[{Main.ModId}] 初始遗物加强：找不到 {type.Name}.{method}，跳过");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(StarterRelicRework), prefixName)));
    }

    private static void PatchGetter(Harmony harmony, Type type, string property, string prefixName)
    {
        MethodInfo? getter = AccessTools.PropertyGetter(type, property);
        if (getter is null)
        {
            Main.Log.Warn($"[{Main.ModId}] 初始遗物加强：找不到 {type.Name}.{property}，跳过");
            return;
        }

        harmony.Patch(getter, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(StarterRelicRework), prefixName)));
    }

    private static Task SummonAsync(RelicModel relic, decimal amount)
    {
        return OstyCmd.Summon(new ThrowingPlayerChoiceContext(), relic.Owner, amount, relic);
    }

    private static int TurnNumber(RelicModel relic)
    {
        return relic.Owner?.PlayerCombatState?.TurnNumber ?? 1;
    }

    // ---------------- 缚魂命匣 ----------------

    private static bool BoundVarsPrefix(ref IEnumerable<DynamicVar> __result)
    {
        // 描述里只写「前三个回合 2 点」，之后回合的 1 点在文案里文字说明
        __result = [new SummonVar(BoundBoostedSummon)];
        return false;
    }

    private static bool BoundBeforeCombatStartPrefix(BoundPhylactery __instance, ref Task __result)
    {
        // 第一回合的召唤在这里发生
        __result = SummonAsync(__instance, BoundBoostedSummon);
        return false;
    }

    private static bool BoundAfterEnergyResetLatePrefix(BoundPhylactery __instance, Player player, ref Task __result)
    {
        // 别的玩家（联机）交回原版处理
        if (player != __instance.Owner) return true;

        int turn = TurnNumber(__instance);
        if (turn <= 1) return true;   // 第一回合已经由 BeforeCombatStart 给过

        decimal amount = turn <= BoostedTurns ? BoundBoostedSummon : BoundNormalSummon;
        __result = SummonAsync(__instance, amount);
        return false;
    }

    // ---------------- 无界命匣 ----------------

    private static bool UnboundVarsPrefix(ref IEnumerable<DynamicVar> __result)
    {
        __result =
        [
            new SummonVar("StartOfCombat", UnboundCombatStartSummon),
            new SummonVar("StartOfTurn", UnboundTurnSummon),
        ];
        return false;
    }

    private static bool UnboundAfterSideTurnStartPrefix(
        PhylacteryUnbound __instance,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState,
        ref Task __result)
    {
        if (!participants.Contains(__instance.Owner.Creature)) return true;

        // 第一回合的 8 点已经在 BeforeCombatStart 给过，这里不能再叠 3
        if (TurnNumber(__instance) <= 1)
        {
            __result = Task.CompletedTask;
            return false;
        }

        __result = SummonAsync(__instance, UnboundTurnSummon);
        return false;
    }
}
