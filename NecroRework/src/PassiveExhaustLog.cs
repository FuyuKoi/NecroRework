using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

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
    }

    private static void ExhaustPrefix(CardModel card, bool causedByEthereal)
    {
        try
        {
            if (card == null) return;

            Player? player = card.Owner;
            if (player == null) return;

            // 正在被打出的牌 = 主动消耗，不算「被动」
            if (PullFromBelowRedirect.IsBeingPlayed(card)) return;

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

        List<CardModel> picked = list
            .Where(e => e.Turn == turn - 1)
            .Select(e => e.Card)
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
