using System;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace NecroRework;

/// <summary>
/// 彼岸一瞥用：给生成的「灵魂」随机附一个**非攻击类**的附魔。
/// 「非攻击类」= 不会像腐化那样只给攻击牌加伤害的那些；这里挑的全是灵魂（0 费技能）能吃、
/// 而且效果是增益的附魔：格挡、固有+保留、减费、去掉消耗、回能、保留、抽牌。
/// （「墨染/Inky」被用户点名移除，不再进池子。）
/// </summary>
internal static class SoulEnchantmentRoll
{
    private static readonly Func<EnchantmentModel>[] Pool =
    [
        () => ModelDb.Enchantment<Adroit>(),
        () => ModelDb.Enchantment<RoyallyApproved>(),
        () => ModelDb.Enchantment<SlumberingEssence>(),
        () => ModelDb.Enchantment<SoulsPower>(),
        () => ModelDb.Enchantment<Sown>(),
        () => ModelDb.Enchantment<Steady>(),
        () => ModelDb.Enchantment<Swift>(),
    ];

    internal static void ApplyRandom(CardModel card, Player? roller)
    {
        try
        {
            int index = 0;
            try
            {
                index = roller?.RunState.Rng.CombatCardGeneration.NextInt(Pool.Length) ?? 0;
            }
            catch
            {
                index = 0;
            }

            if (index < 0 || index >= Pool.Length)
            {
                index = 0;
            }

            EnchantmentModel canonical = Pool[index]();
            CardCmd.Enchant(canonical.ToMutable(), card, 1m);
            Main.Log.Info($"彼岸一瞥：为 {card.Id} 附上随机附魔 {canonical.Id}");
        }
        catch (Exception ex)
        {
            Main.Log.Warn($"彼岸一瞥：附魔失败（{ex.GetType().Name}: {ex.Message}），跳过这张");
        }
    }
}
