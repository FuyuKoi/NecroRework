using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace NecroRework;

/// <summary>
/// 记录「被动消耗的虚无牌」：每个玩家一份，按消耗先后排序，只保留最近两个回合。
/// 判定口径和亡魂牵引一致 —— 不是正在打出的牌（<c>PlayingCards</c>）、牌上带「虚无」。
/// </summary>
internal static class PassiveExhaustLog
{
    internal readonly record struct Entry(CardModel Card, int Turn);

    private static readonly Dictionary<Player, List<Entry>> Log = new();

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Exhaust));
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 CardCmd.Exhaust，初心的记录不会生效");
            return;
        }

        harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(PassiveExhaustLog), nameof(ExhaustPrefix)));

        // 每场战斗开始时清空：回合数每场战斗都从头数起，
        // 不清就会把上一场战斗里消耗掉的牌当成本场「上回合消耗的牌」拉回来。
        MethodInfo? combatStart = AccessTools.Method(typeof(Hook), nameof(Hook.BeforeCombatStart),
            [typeof(IRunState), typeof(ICombatState)]);

        if (combatStart == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 初心的被动消耗记录：找不到 Hook.BeforeCombatStart，跨战斗不会自动清空");
        }
        else
        {
            harmony.Patch(combatStart,
                postfix: new HarmonyMethod(typeof(PassiveExhaustLog), nameof(BeforeCombatStartPostfix)));
        }
    }

    private static void BeforeCombatStartPostfix()
    {
        try
        {
            Log.Clear();
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 清空被动消耗记录失败: {e.Message}");
        }
    }

    private static void ExhaustPrefix(CardModel card, bool causedByEthereal)
    {
        try
        {
            if (card == null) return;

            Player? player = card.Owner;
            if (player == null) return;

            // 正在被打出的牌 = 主动消耗，不算「被动」：
            // ①正在 OnPlayWrapper 内（PlayingCards）、②牌此刻还在「打出中」牌堆里
            if (PullFromBelowRedirect.IsBeingPlayed(card)) return;
            if (card.Pile?.Type == PileType.Play) return;

            if (!card.Keywords.Contains(CardKeyword.Ethereal)) return;

            int turn = player.PlayerCombatState?.TurnNumber ?? 0;

            List<Entry> list = Get(player);
            list.Add(new Entry(card, turn));

            // 只留最近两个回合，避免长战斗里堆一堆失效引用
            list.RemoveAll(e => e.Turn < turn - 1);
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 被动消耗记录失败: {e}");
        }
    }

    /// <summary>取「上回合最先被动消耗的至多 N 张虚无牌」，取走后清掉这些记录。</summary>
    public static List<CardModel> TakeLastTurn(Player player, int count)
    {
        int turn = player.PlayerCombatState?.TurnNumber ?? 0;
        List<Entry> list = Get(player);
        ICombatState? state = player.Creature.CombatState;

        // 只认「现在还在这场战斗里、而且属于这个玩家」的牌。
        // 跨战斗的残留引用一律丢掉，否则会被塞回手牌、然后在回合结束时
        // 触发引擎的「must be added to a CombatState」异常（整条回合循环会死）。
        List<CardModel> picked = list
            .Where(e => e.Turn == turn - 1)
            .Select(e => e.Card)
            .Where(card => card.Owner == player)
            .Where(card => state == null || state.ContainsCard(card))
            .Take(count)
            .ToList();

        // 上回合（及更早）的记录都用完就清掉，同一张牌不会被重复拉回来
        list.RemoveAll(e => e.Turn <= turn - 1);

        return picked;
    }

    private static List<Entry> Get(Player player)
    {
        if (!Log.TryGetValue(player, out List<Entry>? list))
        {
            list = [];
            Log[player] = list;
        }

        return list;
    }
}
