using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace NecroRework;

/// <summary>
/// 卡牌大全（图鉴）滚动越界的兜底。
///
/// 原版 <see cref="NCardGrid"/> 的无限滚动窗口里，<c>ReallocateAbove</c> 结尾写
/// <c>List&lt;NGridCardHolder&gt; list = _cardRows[1];</c>，<c>ReallocateBelow</c> 同样按
/// 「至少两行」的假设取行；而 <c>AllocateCardHolders</c> 会直接读 <c>_cardRows[0][0]</c> 与
/// <c>_cardRows[最后][0]</c>。卡牌只有一行（或某一行是空的）时这些下标就越界，抛
/// <see cref="ArgumentOutOfRangeException"/>，图鉴那一页的布局随即中断——表现就是「卡牌显示不全 / 出错误」。
///
/// 我们只加卡不改网格，但卡变多以后更容易踩到这条路径。守卫只做一件事：
/// 在「行数不足两行」或「首尾行是空行」时跳过那个必然越界的重排（这种状态下本来也没有可滚动的行）。
/// </summary>
internal static class CardGridGuard
{
    private static readonly AccessTools.FieldRef<NCardGrid, List<List<NGridCardHolder>>> Rows =
        AccessTools.FieldRefAccess<NCardGrid, List<List<NGridCardHolder>>>("_cardRows");

    private static int _reported;

    public static void Install(Harmony harmony)
    {
        // 需要「至少两行」才有意义的重排
        Patch(harmony, "ReallocateAbove", nameof(SkipWhenFewerThanTwoRows));
        Patch(harmony, "ReallocateBelow", nameof(SkipWhenFewerThanTwoRows));
        // 会直接读第 0 行 / 最后一行第一个格子的那个入口
        Patch(harmony, "AllocateCardHolders", nameof(SkipWhenEdgeRowEmpty));

        Main.Log.Info($"[{Main.ModId}] 卡牌大全网格守卫已挂载");
    }

    private static void Patch(Harmony harmony, string methodName, string prefixName)
    {
        var target = AccessTools.Method(typeof(NCardGrid), methodName);
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 卡牌大全网格守卫：找不到 NCardGrid.{methodName}，已跳过");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(typeof(CardGridGuard), prefixName));
    }

    private static void Report(string what, List<List<NGridCardHolder>> rows)
    {
        if (_reported >= 10)
        {
            return;
        }

        _reported++;
        Main.Log.Warn($"[{Main.ModId}] 卡牌大全网格：{what}（行数 {rows.Count}），已跳过本次重排以避开原版越界");
    }

    /// <summary>返回 false 表示跳过原逻辑。</summary>
    private static bool SkipWhenFewerThanTwoRows(NCardGrid __instance)
    {
        try
        {
            List<List<NGridCardHolder>>? rows = Rows(__instance);
            if (rows is null)
            {
                return true;
            }

            if (rows.Count >= 2)
            {
                return true;
            }

            Report("只有不到两行", rows);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 卡牌大全网格守卫出错，放行原逻辑：{e.Message}");
            return true;
        }
    }

    /// <summary>返回 false 表示跳过原逻辑。</summary>
    private static bool SkipWhenEdgeRowEmpty(NCardGrid __instance)
    {
        try
        {
            List<List<NGridCardHolder>>? rows = Rows(__instance);
            if (rows is null || rows.Count == 0)
            {
                // 还没有分配任何行，交给原逻辑去建行
                return true;
            }

            if (rows[0].Count == 0 || rows[rows.Count - 1].Count == 0)
            {
                Report("首行或末行是空行", rows);
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 卡牌大全网格守卫出错，放行原逻辑：{e.Message}");
            return true;
        }
    }
}
