using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 升华的「耗能变化」记账：
///  1) 打出过一次之后再抽到这张牌 → 它的耗能永久 +1；
///  2) 任何让这张牌当前耗能变化的来源（这条 +1、飘荡附魔的 +1、交换的临时 -1……）
///     都会被算成「变化了几点」，伤害按点数累加。
/// 观测量取 <see cref="CardEnergyCost.GetWithModifiers"/> 在 CostModifiers.All 下的结果，
/// 并对同一张牌缓存上一次的值，只在真的变了的时候记账。
/// </summary>
internal static class SublimationTracker
{
    private sealed class CostState
    {
        public int Value;

        public bool Initialized;
    }

    private static readonly ConditionalWeakTable<CardModel, CostState> CostStates = new();

    private static readonly AccessTools.FieldRef<CardEnergyCost, CardModel> CardRef =
        AccessTools.FieldRefAccess<CardEnergyCost, CardModel>("_card");

    public static void Install(Harmony harmony)
    {
        try
        {
            var drawn = AccessTools.Method(typeof(CardModel), nameof(CardModel.InvokeDrawn));
            if (drawn != null)
            {
                harmony.Patch(drawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(DrawnPostfix))));
            }

            var getCost = AccessTools.Method(typeof(CardEnergyCost), nameof(CardEnergyCost.GetWithModifiers));
            if (getCost != null)
            {
                harmony.Patch(getCost, postfix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(CostEvaluatedPostfix))));
            }

            Main.Log.Info($"[{Main.ModId}] 升华记账已挂载");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华记账挂载失败: {e}");
        }
    }

    /// <summary>打出过一次之后，每次再抽到它就永久 +1 费。</summary>
    private static void DrawnPostfix(CardModel __instance)
    {
        try
        {
            if (__instance is not Sublimation) return;
            if (!Sublimation.HasBeenPlayed(__instance)) return;

            __instance.EnergyCost.AddThisCombat(1);
            __instance.InvokeEnergyCostChanged();
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华抽牌加费失败: {e}");
        }
    }

    /// <summary>任何耗能变化都折算成伤害步进。</summary>
    private static void CostEvaluatedPostfix(CardEnergyCost __instance, CostModifiers modifiers, int __result)
    {
        try
        {
            if (modifiers != CostModifiers.All) return;

            CardModel card = CardRef(__instance);
            if (card is not Sublimation) return;
            if (card.Pile?.Type == PileType.Play) return;

            if (!CostStates.TryGetValue(card, out CostState state))
            {
                state = new CostState();
                CostStates.Add(card, state);
            }

            if (!state.Initialized)
            {
                state.Value = __result;
                state.Initialized = true;
                return;
            }

            if (__result == state.Value) return;

            int delta = Math.Abs(__result - state.Value);
            state.Value = __result;
            Sublimation.AddCostSteps(card, delta);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华耗能记账失败: {e}");
        }
    }
}
