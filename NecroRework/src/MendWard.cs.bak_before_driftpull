using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 「弥合」的临时保护：被点名的虚无牌在**本回合**不会因为「虚无」而自我消耗。
/// <para>
/// 引擎在回合结束时按 <c>card.Keywords.Contains(CardKeyword.Ethereal)</c> 判定要不要烧掉，
/// 具体判定藏在 <see cref="Hook.ShouldEtherealTrigger"/> 里，所以这里挂它的 prefix 直接返回 false。
/// </para>
/// </summary>
internal static class MendWard
{
    private static readonly HashSet<CardModel> Suppressed = new(ReferenceEqualityComparer.Instance);

    public static void SuppressEthereal(CardModel card)
    {
        Suppressed.Add(card);
    }

    public static void Install(Harmony harmony)
    {
        MethodInfo? trigger = AccessTools.Method(typeof(Hook), nameof(Hook.ShouldEtherealTrigger));
        if (trigger is null)
        {
            Main.Log.Warn($"[{Main.ModId}] 弥合：找不到 Hook.ShouldEtherealTrigger，跳过");
            return;
        }

        harmony.Patch(trigger, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(MendWard), nameof(ShouldEtherealTriggerPrefix))));

        // 新回合开始时清掉上一回合的标记（保留/不消耗都只持续一个回合）
        MethodInfo? turnStart = AccessTools.Method(typeof(Hook), "AfterPlayerTurnStart");
        if (turnStart != null)
        {
            harmony.Patch(turnStart, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(MendWard), nameof(TurnStartPostfix))));
        }
    }

    private static bool ShouldEtherealTriggerPrefix(CardModel card, ref bool __result)
    {
        if (!NecroSettings.ExtraCards) return true;

        if (card is null || !Suppressed.Contains(card))
        {
            return true;
        }

        __result = false;
        return false;
    }

    private static void TurnStartPostfix()
    {
        Suppressed.Clear();
    }
}
