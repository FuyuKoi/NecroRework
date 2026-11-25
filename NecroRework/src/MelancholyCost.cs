using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 忧郁：每当你或奥斯提获得负面效果时，这张牌在本场战斗中的耗能永久 -1。
/// 一次「施加事件」只算 1 点（比如一次给 2 层易伤也只 -1）。
/// </summary>
internal static class MelancholyCost
{
    private static readonly PileType[] CombatPiles =
    [
        PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play,
    ];

    /// <summary>
    /// 「同一次施加」的去重表：键 = (谁的牌, 减益 ID, 施加者)，按**回合**清空。
    ///
    /// 这里刻意**不使用墙钟时间**（旧版用 400ms 窗口）：两台机器收到回调的时刻不可能完全一致，
    /// 用时间窗口会让「一台判成同一次、另一台判成两次」，联机时两张忧郁的费用就会不一样。
    /// 改成「同一个回合内，同一种减益（同一施加者）只降 1 次费」之后，
    /// 判断只依赖双方都同步的回合数/减益本身，结果是确定的。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<(Player Player, string PowerId, int ApplierId)> DiscountedThisTurn = [];

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Hook), "AfterPowerAmountChanged");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterPowerAmountChanged，忧郁的降费不会生效");
            return;
        }

        harmony.Patch(target,
            postfix: new HarmonyMethod(typeof(MelancholyCost), nameof(AfterPowerAmountChangedPostfix)));

        // 每回合清空去重表（玩家的回合开始 = 新一轮「被打」）
        var turnStart = AccessTools.Method(typeof(Hook), "AfterPlayerTurnStart");
        if (turnStart != null)
        {
            harmony.Patch(turnStart,
                postfix: new HarmonyMethod(typeof(MelancholyCost), nameof(TurnStartPostfix)));
        }

        var combatStart = AccessTools.Method(typeof(Hook), "BeforeCombatStart");
        if (combatStart != null)
        {
            harmony.Patch(combatStart,
                postfix: new HarmonyMethod(typeof(MelancholyCost), nameof(TurnStartPostfix)));
        }

        Main.Log.Info($"[{Main.ModId}] 忧郁降费去重已挂载（按回合 + 减益类型，不依赖时间）");
    }

    /// <summary>回合/战斗开始：清空去重表。</summary>
    private static void TurnStartPostfix()
    {
        DiscountedThisTurn.Clear();
    }

    private static void AfterPowerAmountChangedPostfix(PowerModel power, decimal amount, Creature applier)
    {
        if (!NecroSettings.CardReworks) return;

        try
        {
            if (power == null || amount <= 0m) return;
            if (power.Type != PowerType.Debuff) return;

            var owner = power.Owner;
            var player = owner?.Player ?? owner?.PetOwner;
            if (player == null) return;

            int applierKey = applier?.CombatId is { } cid ? unchecked((int)cid) : -1;

            // 同一回合内，同一种减益（同一施加者）只算一次 —— 怪物一次行动同时甩给
            // 玩家和奥斯提时，第二份不会重复降费。
            if (!DiscountedThisTurn.Add((player, power.Id.ToString(), applierKey)))
            {
                return;
            }

            foreach (var pileType in CombatPiles)
            {
                var pile = pileType.GetPile(player);
                if (pile == null) continue;

                foreach (var card in pile.Cards)
                {
                    if (card is not Melancholy) continue;

                    card.EnergyCost.AddThisCombat(-1);
                    card.InvokeEnergyCostChanged();
                }
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 忧郁降费失败: {e}");
        }
    }
}
