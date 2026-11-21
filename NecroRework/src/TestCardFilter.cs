using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 调试卡「飘荡实验」不应该出现在正常游戏里。
/// 卡池统一通过 <see cref="CardPoolModel.GetUnlockedCards"/> 供给所有获取渠道
/// （战斗奖励、商店、药水、随机生成卡等），所以在这一步把它筛掉即可：
/// 卡本身仍留在卡池里（控制台/卡牌图鉴照常可见可调试），但玩家永远抽不到。
/// </summary>
internal static class TestCardFilter
{
    public static void Install(Harmony harmony)
    {
        try
        {
            System.Reflection.MethodInfo method = AccessTools.Method(typeof(CardPoolModel), "GetUnlockedCards");
            if (method == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardPoolModel.GetUnlockedCards，调试卡仍可能出现在奖励里");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(typeof(TestCardFilter), nameof(GetUnlockedCardsPostfix))));
            Main.Log.Info($"[{Main.ModId}] 调试卡获取过滤已挂载");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 调试卡获取过滤挂载失败: {e}");
        }
    }

    private static void GetUnlockedCardsPostfix(ref IEnumerable<CardModel> __result)
    {
        if (__result == null) return;

        List<CardModel> cards = __result.ToList();
        int removed = cards.RemoveAll(static card => card is DriftTestCard);
        if (removed > 0)
            __result = cards;
    }
}
