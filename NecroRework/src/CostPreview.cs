using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 选牌预览用的「费用还原」开关。
/// 墓友这类「按费用筛选抽牌堆」的牌，在弹选牌框期间希望看到卡牌**原本的费用**：
/// 预借时间（<c>BorrowedTimePower</c>，全局 +N 费）不应该给抽牌堆里的牌加价，
/// 否则既显示成更贵、也筛不进来。
/// 打开开关后，抽牌堆里的牌在计算费用时会跳过全局费用钩子（<c>Hook.ModifyEnergyCostInCombat</c> /
/// <c>...Late</c>）；手牌 / 打出区的牌不受影响，因为墓友自己的「本次费用」要带上限效果。
/// </summary>
public static class CostPreview
{
    [ThreadStatic] private static int _depth;

    public static bool Active => _depth > 0;

    public static void Begin() => _depth++;

    public static void End()
    {
        if (_depth > 0) _depth--;
    }

    /// <summary>只对「躺在抽牌堆里」的牌生效。</summary>
    public static bool ShouldRestore(CardModel? card) =>
        Active && card?.Pile?.Type == PileType.Draw;

    public static void Install(Harmony harmony)
    {
        // 早/晚两次费用修正都发生在这个静态方法内部（Late 是在它里面遍历监听者调用的），
        // 所以只需在入口处拦一次，就同时覆盖预借时间这类「+N 费」的全局修正。
        var hook = AccessTools.Method(typeof(Hook), nameof(Hook.ModifyEnergyCostInCombat));
        harmony.Patch(hook, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(CostPreview), nameof(SkipPrefix))));

        Main.Log.Info("[NecroRework] 费用预览还原已挂载（选牌期间抽牌堆的牌不受全局加费影响）");
    }

    private static bool SkipPrefix(CardModel card, decimal originalCost, ref decimal __result)
    {
        if (!NecroSettings.ExtraCards) return true;

        if (!ShouldRestore(card)) return true;

        __result = originalCost;
        return false;
    }
}
