using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework;

/// <summary>
/// 本场战斗内的累计统计。数据来自游戏自己的战斗历史（CombatHistory），
/// 所以读档/重进战斗都由游戏维护，不用我们自己存档。
/// </summary>
internal static class CombatStats
{
    /// <summary>本场战斗中，该卡牌拥有者累计获得的召唤量。</summary>
    public static int TotalSummon(CardModel? card)
    {
        // 百科/图鉴里拿到的是 canonical 模型，访问 Owner 会抛 CanonicalModelException
        if (card is null || card.IsCanonical) return 0;
        if (card.Owner?.Creature is not { } me) return 0;

        var manager = CombatManager.Instance;
        if (manager?.History == null) return 0;

        var total = 0;
        foreach (var entry in manager.History.Entries)
        {
            if (entry is SummonedEntry summon && summon.Actor == me)
                total += summon.Amount;
        }
        return total;
    }

    /// <summary>每获得过 2 点召唤 → 额外 1 点伤害（向下取整）。</summary>
    public static decimal SummonDamageBonus(CardModel card) => TotalSummon(card) / 2;
}
