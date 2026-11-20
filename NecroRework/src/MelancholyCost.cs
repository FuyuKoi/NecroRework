using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

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
    }

    private static void AfterPowerAmountChangedPostfix(PowerModel power, decimal amount)
    {
        try
        {
            if (power == null || amount <= 0m) return;
            if (power.Type != PowerType.Debuff) return;

            var owner = power.Owner;
            var player = owner?.Player ?? owner?.PetOwner;
            if (player == null) return;

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
