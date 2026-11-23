using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework;

/// <summary>
/// 兜底：保证「躺在战斗牌堆里的牌」确实登记在当前这场战斗的 CombatState 里。
///
/// 引擎在 CardPileCmd.Add 里会检查这件事，没登记就直接抛
/// 「XXX must be added to a CombatState before adding it to this pile」，
/// 这异常一抛整条回合循环就死了 —— 表现就是「结束回合卡住、必须重开房间」。
///
/// 我们的卡（初心把上回合消耗的虚无牌拉回手牌、亡魂牵引自动打出等）是主要可能来源，
/// 所以在这里统一补登记一次，顺手把「已被移出战斗」的标记清掉，让后续的移动/消耗能正常走。
/// </summary>
internal static class CombatCardGuard
{
    /// <summary>这张牌是否已经登记在当前战斗中。</summary>
    internal static bool IsRegistered(CardModel? card)
    {
        try
        {
            if (card is not { IsMutable: true }) return true;

            ICombatState? state = CurrentStateOf(card);
            return state == null || state.ContainsCard(card);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 没登记就补登记。返回 true 表示「现在这张牌可以安全地放进战斗牌堆」。
    /// 对本来就没问题的牌只做一次 ContainsCard，很便宜。
    /// </summary>
    internal static bool EnsureRegistered(CardModel? card, string reason)
    {
        try
        {
            if (card is not { IsMutable: true }) return true;

            Player? owner = card.Owner;
            ICombatState? state = CurrentStateOf(card);
            if (owner == null || state == null) return true;

            if (state.ContainsCard(card)) return true;

            if (card.HasBeenRemovedFromState)
            {
                card.HasBeenRemovedFromState = false;
            }

            state.AddCard(card, owner);
            Main.Log.Warn($"[{Main.ModId}] 补登记卡牌 {card.Id} 到当前战斗（来源：{reason}）");
            return true;
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 补登记卡牌失败（来源：{reason}）: {e.Message}");
            return false;
        }
    }

    private static ICombatState? CurrentStateOf(CardModel card)
    {
        ICombatState? state = card.CombatState;
        if (state != null) return state;

        return card.Owner?.Creature.CombatState;
    }
}
