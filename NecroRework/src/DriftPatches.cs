using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;

namespace NecroRework;

/// <summary>
/// 「飘荡」附魔的三处行为。
/// 原版 EnchantmentModel 没有费用/关键词/消耗的虚方法，所以这里用补丁实现。
/// </summary>
internal static class DriftPatches
{
    internal static bool IsDrift(CardModel card) => card != null && card.Enchantment is DriftEnchantment;

    /// <summary>费用 +1：直接改「最终费用」的计算结果，显示和实际扣费都覆盖。</summary>
    [HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.GetWithModifiers))]
    private static class CostPlusOne
    {
        private static readonly AccessTools.FieldRef<CardEnergyCost, CardModel> CardRef =
            AccessTools.FieldRefAccess<CardEnergyCost, CardModel>("_card");

        [HarmonyPostfix]
        private static void Postfix(CardEnergyCost __instance, ref int __result)
        {
            if (__instance.CostsX) return; // X 费牌不处理
            if (IsDrift(CardRef(__instance))) __result += 1;
        }
    }

    /// <summary>获得「虚无」：在关键词集合上实时叠加，不改卡牌自身状态。</summary>
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.Keywords), MethodType.Getter)]
    private static class AddEthereal
    {
        [HarmonyPostfix]
        private static void Postfix(CardModel __instance, ref IReadOnlySet<CardKeyword> __result)
        {
            if (!IsDrift(__instance)) return;
            if (__result.Contains(CardKeyword.Ethereal)) return;
            __result = new HashSet<CardKeyword>(__result) { CardKeyword.Ethereal };
        }
    }

    /// <summary>消耗时获得 4 点格挡。等原版消耗流程跑完再给，避免顺序问题。</summary>
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Exhaust))]
    private static class BlockOnExhaust
    {
        [ThreadStatic] private static bool _running;

        private static bool Prefix(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal,
                                   bool skipVisuals, ref Task<CardPileAddResult?> __result)
        {
            if (_running || !IsDrift(card)) return true;
            __result = Run(choiceContext, card, causedByEthereal, skipVisuals);
            return false;
        }

        private static async Task<CardPileAddResult?> Run(PlayerChoiceContext ctx, CardModel card,
                                                          bool ethereal, bool skipVisuals)
        {
            _running = true;
            try
            {
                CardPileAddResult? result = await CardCmd.Exhaust(ctx, card, ethereal, skipVisuals);

                if (card.Owner?.Creature is { } owner)
                    await CreatureCmd.GainBlock(owner, DriftEnchantment.BlockOnExhaust, ValueProp.Unpowered, null);

                return result;
            }
            finally
            {
                _running = false;
            }
        }
    }
}
