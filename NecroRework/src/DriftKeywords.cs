using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Enchantments;

namespace NecroRework;

/// <summary>
/// 飘荡的另一半：让附了飘荡的牌在**任何**读取关键词的地方都带上「虚无」。
/// 原来只补了 <c>CardModel.Keywords</c>，但卡面/图鉴/牌堆筛选等地方会直接调
/// <c>GetKeywordsWithSources(KeywordSources.All)</c>，那条路绕过了 <c>Keywords</c>，
/// 于是「卡面上没有虚无、看着也不会被烧」。
/// </summary>
internal static class DriftKeywords
{
    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(
            typeof(CardModel),
            nameof(CardModel.GetKeywordsWithSources),
            [typeof(KeywordSources)]);

        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.GetKeywordsWithSources，飘荡的虚无只能靠旧路径");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(DriftKeywords), nameof(Postfix)));
        Main.Log.Info($"[{Main.ModId}] 飘荡关键词补丁已挂载（GetKeywordsWithSources）");
    }

    private static void Postfix(CardModel __instance, ref IReadOnlySet<CardKeyword> __result)
    {
        try
        {
            if (__instance?.Enchantment is not DriftEnchantment) return;
            if (__result.Contains(CardKeyword.Ethereal)) return;

            var set = new HashSet<CardKeyword>(__result) { CardKeyword.Ethereal };
            __result = set;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡关键词补写失败: {e}");
        }
    }
}
