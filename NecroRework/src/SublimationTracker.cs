using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Cards;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 升华的「耗能变化」记账：
///  1) 打出过一次之后再抽到这张牌 → 它的耗能永久 +1；
///  2) 任何让这张牌当前耗能变化的来源（这条 +1、飘荡附魔的 +1、交换的临时 -1、
///     蛇行/蛇眼的随机重掷……）都算「变化了一次」，伤害按次数累加（一次跳几点都只算一次）。
///
/// 观测量取 <see cref="CardEnergyCost.GetWithModifiers"/> 在 CostModifiers.All 下的结果。
/// 因为蛇行（Slither）和蛇眼（ConfusedPower）都在 Hook.AfterCardDrawn 里把耗能
/// 重掷成 0~3，和我们自己的「再抽到 +1」叠在一次抽牌里，轮询很容易把一次抽牌记成 2 步，
/// 所以抽牌这一段改成「抽牌前拍快照 → 抽牌后比对」，一次抽牌最多记 1 步。
/// </summary>
internal static class SublimationTracker
{
    private sealed class CostState
    {
        public int Value;

        public bool Initialized;

        /// <summary>本次抽牌开始时的耗能（-999 = 没在抽牌窗口里）。</summary>
        public int DrawStart = int.MinValue;

        public bool InDraw;

        /// <summary>本次抽牌里「蛇行 / 混乱（蛇眼）」重掷过耗能。</summary>
        public bool RandomizedThisDraw;

        /// <summary>本次抽牌的「再抽到 +1」是否已经在重掷之前结算过（避免被重复加）。</summary>
        public bool PlayedBonusApplied;

        /// <summary>「再抽到 +1」结算之后的耗能（重掷前），用来判断这次 +1 有没有真的改变数值。</summary>
        public int AfterBonus = int.MinValue;

        /// <summary>这次 +1 是否真的改变了耗能。</summary>
        public bool BonusChanged;

        /// <summary>本次抽牌由「蛇行 / 混乱」重掷折算出来的增伤步数（0~2）。</summary>
        public int RandomizedSteps;
    }

    private static readonly ConditionalWeakTable<CardModel, CostState> CostStates = new();

    private static readonly AccessTools.FieldRef<CardEnergyCost, CardModel> CardRef =
        AccessTools.FieldRefAccess<CardEnergyCost, CardModel>("_card");

    /// <summary>我们自己主动查询耗能时不要再触发记账（否则自己咬自己）。</summary>
    private static bool _inOwnQuery;

    public static void Install(Harmony harmony)
    {
        bool drawnOk = false;
        bool snapshotOk = false;
        bool costOk = false;

        try
        {
            var drawn = AccessTools.Method(typeof(CardModel), nameof(CardModel.InvokeDrawn));
            if (drawn != null)
            {
                harmony.Patch(drawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(DrawnPostfix))));
                drawnOk = true;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华记账：InvokeDrawn 补丁失败: {e}");
        }

        // 抽牌钩子跑完（蛇行/蛇眼已经重掷完）再比对，所以要在 Hook 入口拍快照。
        try
        {
            var afterDrawn = AccessTools.Method(typeof(Hook), nameof(Hook.AfterCardDrawn));
            if (afterDrawn != null)
            {
                harmony.Patch(afterDrawn, prefix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(AfterCardDrawnPrefix))));
                snapshotOk = true;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华记账：AfterCardDrawn 快照失败: {e}");
        }

        try
        {
            var getCost = AccessTools.Method(typeof(CardEnergyCost), nameof(CardEnergyCost.GetWithModifiers));
            if (getCost != null)
            {
                harmony.Patch(getCost, postfix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(CostEvaluatedPostfix))));
                costOk = true;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华记账：耗能轮询失败: {e}");
        }

        // 蛇行 / 混乱（蛇眼）在 AfterCardDrawn 里重掷耗能。改成「先结算升华的 +1，再让它重掷」，
        // 于是最终耗能必定落在 0~3（对玩家有利），但这一次抽牌算两次耗能变化。
        try
        {
            PatchBeforeRandomize(harmony, AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.Enchantments.Slither), "AfterCardDrawn"));
            PatchBeforeRandomize(harmony, AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.Powers.ConfusedPower), "AfterCardDrawn"));
            Main.Log.Info($"[{Main.ModId}] 升华：蛇行 / 混乱的耗能重掷顺序已接管");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华：接管蛇行 / 混乱失败: {e}");
        }

        Main.Log.Info($"[{Main.ModId}] 升华记账已挂载（抽牌 {drawnOk} / 快照 {snapshotOk} / 轮询 {costOk}）");
    }

    private static void PatchBeforeRandomize(Harmony harmony, System.Reflection.MethodBase? method)
    {
        if (method == null) return;

        harmony.Patch(method,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(BeforeRandomizePrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(SublimationTracker), nameof(AfterRandomizePostfix))));
    }

    /// <summary>蛇行 / 混乱重掷之前：先把「再抽到 +1」结算掉，并记下它有没有真的改变耗能。</summary>
    private static void BeforeRandomizePrefix(CardModel card)
    {
        if (!NecroSettings.CardReworks) return;

        try
        {
            if (card is not Sublimation) return;

            CostState state = StateOf(card);
            state.RandomizedThisDraw = true;

            if (state.PlayedBonusApplied) return;
            if (!Sublimation.HasBeenPlayed(card)) return;

            state.PlayedBonusApplied = true;
            state.BonusChanged = false;
            card.EnergyCost.AddThisCombat(1);
            card.InvokeEnergyCostChanged();

            int afterBonus = QueryCost(card);
            state.AfterBonus = afterBonus;
            state.BonusChanged = state.DrawStart == int.MinValue || afterBonus != state.DrawStart;
            Main.Log.Info($"[{Main.ModId}] 升华：{card.Id} 先结算「再抽到 +1」（{state.DrawStart} → {afterBonus}），随后由随机耗能覆盖");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华重掷前结算失败: {e}");
        }
    }

    /// <summary>
    /// 重掷之后只做「标记」，真正的步数在抽牌收尾时按最终值算。
    /// 这样两张随机源（蛇行 + 蛇眼）同时存在也不会把步骤数算乱。
    /// </summary>
    private static void AfterRandomizePostfix(CardModel card)
    {
        if (!NecroSettings.CardReworks) return;

        try
        {
            if (card is not Sublimation) return;

            CostState state = StateOf(card);
            state.RandomizedThisDraw = true;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华重掷标记失败: {e}");
        }
    }

    private static CostState StateOf(CardModel card)
    {
        if (!CostStates.TryGetValue(card, out CostState state))
        {
            state = new CostState();
            CostStates.Add(card, state);
        }

        return state;
    }

    /// <summary>抽牌钩子入口：记下「抽牌之前」的耗能。</summary>
    private static void AfterCardDrawnPrefix(CardModel card)
    {
        if (!NecroSettings.CardReworks) return;

        try
        {
            if (card is not Sublimation) return;

            CostState state = StateOf(card);
            state.DrawStart = QueryCost(card);
            state.InDraw = true;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华抽牌快照失败: {e}");
        }
    }

    /// <summary>抽牌收尾：有蛇行 / 混乱时记 2 步，否则补 +1 并和快照比对（最多 1 步）。</summary>
    private static void DrawnPostfix(CardModel __instance)
    {
        if (!NecroSettings.CardReworks) return;

        try
        {
            if (__instance is not Sublimation) return;

            CostState state = StateOf(__instance);
            state.InDraw = false;

            bool randomized = state.RandomizedThisDraw;
            state.RandomizedThisDraw = false;

            if (!randomized && Sublimation.HasBeenPlayed(__instance))
            {
                // 没有蛇行 / 混乱时，这里追加的 Relative(+1) 会正常累积。
                __instance.EnergyCost.AddThisCombat(1);
                __instance.InvokeEnergyCostChanged();
            }

            int end = QueryCost(__instance);
            int steps = 0;

            if (randomized)
            {
                // 已经有了 +1 时，基线是「+1 之后」的值；没打出过则以抽牌前的值为基线。
                int baseline = state.BonusChanged && state.AfterBonus != int.MinValue ? state.AfterBonus : state.DrawStart;
                steps = (state.BonusChanged ? 1 : 0) + ((baseline == int.MinValue || end != baseline) ? 1 : 0);
                steps = Math.Clamp(steps, 0, 2);
                Main.Log.Info($"[{Main.ModId}] 升华：随机耗能基线 {baseline} → 最终 {end}，本次记 {steps} 步（共 {Sublimation.CostStepsOf(__instance) + steps} 步）");
            }
            else if (state.DrawStart != int.MinValue && end != state.DrawStart)
            {
                steps = 1;
                Main.Log.Info($"[{Main.ModId}] 升华：抽牌耗能 {state.DrawStart} → {end}，记 1 步（共 {Sublimation.CostStepsOf(__instance) + 1} 步）");
            }

            if (steps > 0)
            {
                Sublimation.AddCostSteps(__instance, steps);
            }

            state.Value = end;
            state.Initialized = true;
            state.DrawStart = int.MinValue;
            state.AfterBonus = int.MinValue;
            state.BonusChanged = false;
            state.PlayedBonusApplied = false;
            state.RandomizedSteps = 0;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华抽牌加费失败: {e}");
        }
    }

    /// <summary>抽牌之外的任何耗能变化（交换的临时 -1、升级、药水等）都折算成 1 步。</summary>
    private static void CostEvaluatedPostfix(CardEnergyCost __instance, CostModifiers modifiers, int __result)
    {
        if (!NecroSettings.CardReworks) return;
        if (_inOwnQuery) return;

        try
        {
            if (modifiers != CostModifiers.All) return;

            CardModel card = CardRef(__instance);
            if (card is not Sublimation) return;
            if (card.Pile?.Type == PileType.Play) return;

            CostState state = StateOf(card);

            // 抽牌窗口内的变化由 DrawnPostfix 统一结算，这里只跟着更新缓存。
            if (state.InDraw)
            {
                state.Value = __result;
                return;
            }

            if (!state.Initialized)
            {
                state.Value = __result;
                state.Initialized = true;
                return;
            }

            if (__result == state.Value) return;

            state.Value = __result;

            // 规则是「每变化一次」，不是「每变化 1 点」——所以哪怕一次跳了 2 点，也只记 1 步。
            Sublimation.AddCostSteps(card, 1);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 升华耗能记账失败: {e}");
        }
    }

    private static int QueryCost(CardModel card)
    {
        _inOwnQuery = true;
        try
        {
            return card.EnergyCost.GetWithModifiers(CostModifiers.All);
        }
        finally
        {
            _inOwnQuery = false;
        }
    }
}
