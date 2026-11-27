using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Enchantments;

/// <summary>
/// 飘荡：费用 +1，获得[虚无]，消耗时获得 4 点格挡。
/// 可以附在任何卡牌上——包括状态牌、诅咒、任务牌（原版会拦掉这些，这里整个覆盖掉）。
/// </summary>
public sealed class DriftEnchantment : EnchantmentModel
{
    public const int BlockOnExhaust = 4;

    /// <summary>只为了让描述里的 {Energy:energyIcons()} 能画出能量图标（数值固定 1）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    /// <summary>飘荡不显示层数。</summary>
    public override bool ShowAmount => false;

    /// <summary>任意卡牌都能附；但一张牌同时只能有一个附魔。</summary>
    public override bool CanEnchant(CardModel card)
    {
        if (card == null) return false;
        return card.Enchantment == null;
    }
}
