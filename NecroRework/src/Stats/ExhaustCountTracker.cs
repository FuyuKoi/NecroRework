using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Stats;

/// <summary>
/// 「本场战斗一共消耗了几张牌」的统计，给守护者这类「每消耗 1 张牌 +X」的卡用。
/// 挂在 <c>Hook.AfterCardExhausted</c> 上（所有消耗路径最后都会经过它），战斗开始时清零。
/// </summary>
internal static class ExhaustCountTracker
{
    private static readonly Dictionary<Player, int> Counts = new();

    public static void Install(Harmony harmony)
    {
        MethodInfo? exhausted = AccessTools.Method(typeof(Hook), nameof(Hook.AfterCardExhausted));
        MethodInfo? start = AccessTools.Method(typeof(Hook), nameof(Hook.BeforeCombatStart));

        if (exhausted == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterCardExhausted，消耗统计未挂载");
            return;
        }

        harmony.Patch(exhausted, postfix: new HarmonyMethod(typeof(ExhaustCountTracker), nameof(AfterCardExhaustedPostfix)));

        if (start != null)
        {
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(ExhaustCountTracker), nameof(BeforeCombatStartPrefix)));
        }

        Main.Log.Info($"[{Main.ModId}] 消耗统计已挂载（战斗开始清零 {start != null}）");
    }

    private static void BeforeCombatStartPrefix()
    {
        Counts.Clear();
    }

    private static void AfterCardExhaustedPostfix(CardModel card)
    {
        try
        {
            if (card?.Owner is not { } player) return;

            Counts.TryGetValue(player, out int current);
            Counts[player] = current + 1;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 消耗统计失败: {e}");
        }
    }

    public static int TotalFor(Player? player)
    {
        if (player == null) return 0;
        return Counts.TryGetValue(player, out int value) ? value : 0;
    }
}
