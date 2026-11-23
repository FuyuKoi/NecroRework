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
    /// <summary>
    /// 只记「这是哪张牌 + 是否升级过」，不留牌实例本身。
    /// 被消耗的牌会从 CombatState 里摘掉，留着实例既不能塞回手牌（引擎会抛
    /// 「must be added to a CombatState」），也没法跨回合复用 —— 回收时重建一张新的。
    /// </summary>
    internal readonly record struct Entry(ModelId Id, bool Upgraded, int Turn);

    private static readonly Dictionary<Player, List<Entry>> Log = new();

    public static void Install(Harmony harmony)
    {
        // 记录点不在这里挂：回合结束时虚无牌自烧走的是 CardPileCmd.Add(..., PileType.Exhaust, ...)，
        // 根本不经过 CardCmd.Exhaust（见 PullFromBelowRedirect.ExhaustAddPrefix 里的注释），
        // 所以真正的记录由那边在「确定要真的消耗」之后回调 Record()。

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

    /// <summary>
    /// 由 <see cref="PullFromBelowRedirect"/> 的「进消耗堆」漏斗调用：
    /// 只在牌**真的会被消耗**（没被亡魂牵引改成自动打出）时记录。
    /// 调用方已判过「虚无 + 不在打出中 + 牌堆是手牌/抽牌堆/弃牌堆」。
    /// </summary>
    internal static void Record(CardModel card)
    {
        try
        {
            if (card == null) return;

            Player? player = card.Owner;
            if (player == null) return;

            int turn = player.PlayerCombatState?.TurnNumber ?? 0;

            List<Entry> list = Get(player);
            list.Add(new Entry(card.Id, card.IsUpgraded, turn));

            // 只留最近两个回合，避免长战斗里堆一堆失效引用
            list.RemoveAll(e => e.Turn < turn - 1);
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 被动消耗记录失败: {e}");
        }
    }

    /// <summary>
    /// 取「上回合最先被动消耗的至多 N 张虚无牌」，取走后清掉这些记录。
    /// 返回的是**新建**的牌（已登记到当前 CombatState）——被消耗的旧实例已经离开战斗，
    /// 直接用它会触发引擎异常，也会让回合结束燃烧时卡死。
    /// </summary>
    public static List<CardModel> TakeLastTurn(Player player, int count)
    {
        int turn = player.PlayerCombatState?.TurnNumber ?? 0;
        List<Entry> list = Get(player);
        ICombatState? state = player.Creature.CombatState;
        if (state == null) return [];

        List<Entry> picked = list
            .Where(e => e.Turn == turn - 1)
            .Take(count)
            .ToList();

        // 上回合（及更早）的记录都用完就清掉，同一张牌不会被重复拉回来
        list.RemoveAll(e => e.Turn <= turn - 1);

        var result = new List<CardModel>();
        foreach (Entry entry in picked)
        {
            CardModel? canonical = ModelDb.GetByIdOrNull<CardModel>(entry.Id);
            if (canonical == null) continue;

            CardModel fresh = state.CreateCard(canonical, player);
            if (entry.Upgraded) fresh.UpgradeInternal();
            result.Add(fresh);
        }

        return result;
    }

    /// <summary>诊断用：把当前记录摘成一行（回合号 + 牌 ID），方便排查「为什么没捞到牌」。</summary>
    internal static string Dump(Player player)
    {
        try
        {
            List<Entry> list = Get(player);
            int turn = player.PlayerCombatState?.TurnNumber ?? 0;
            string body = list.Count == 0
                ? "空"
                : string.Join(" / ", list.Select(e => $"{e.Id}(T{e.Turn})"));
            return $"当前回合={turn} 记录{list.Count}条：{body}";
        }
        catch (System.Exception e)
        {
            return $"记录读取失败: {e.Message}";
        }
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
