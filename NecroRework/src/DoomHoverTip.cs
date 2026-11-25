using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework;

/// <summary>
/// 让「描述里提到灾厄、但自身并不施加灾厄」的卡，在悬停/查看时也弹出【灾厄】的说明窗。
///
/// 原版只会为 DynamicVars 里真正带 <see cref="DoomPower"/> 的牌自动加这个提示
/// （比如「洁净」的 {DoomPower} 就会自动出现），而「触碰」「生与死」「大限已至」这类
/// 只是「读」灾厄层数的牌不会自动加，玩家看不到灾厄到底是怎么结算的。
/// </summary>
internal static class DoomHoverTip
{
    /// <summary>描述里会出现「灾厄」、但自身没有 DoomPower 变量的卡。</summary>
    private static readonly HashSet<Type> MentionsDoom = new()
    {
        // 原版卡
        typeof(Defile),
        typeof(BlightStrike),
        typeof(Wisp),
        typeof(Scourge),
        typeof(Afterlife),
        typeof(Defy),
        typeof(PullAggro),
        typeof(Parse),
        typeof(Countdown),
        typeof(EnfeeblingTouch),
        typeof(Deathbringer),
        typeof(Sacrifice),
        typeof(TimesUp),
        typeof(Reanimate),
        typeof(Cleanse),
        // 本 mod 的卡
        typeof(Cards.Touch),
        typeof(Cards.Exchange),
        typeof(Cards.CraftSpirit),
        typeof(Cards.Continuation),
        typeof(Cards.Drag),
        typeof(Cards.OminousPunch),
        typeof(Cards.LifeAndDeath),
        typeof(Cards.Forsake),
        typeof(Cards.RiverOfOblivion),
    };

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.PropertyGetter(typeof(CardModel), "HoverTips");
        if (target == null)
        {
            Main.Log.Error($"[{Main.ModId}] 灾厄悬停提示：找不到 CardModel.HoverTips");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(DoomHoverTip), nameof(HoverTipsPostfix))
        {
            priority = Priority.Low
        });

        Main.Log.Info($"[{Main.ModId}] 灾厄悬停提示已挂载（{MentionsDoom.Count} 张卡）");
    }

    private static void HoverTipsPostfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        try
        {
            if (__instance == null) return;
            if (!MentionsDoom.Contains(__instance.GetType())) return;

            // 自己就带 DoomPower 变量的牌，原版已经加过提示，不要重复
            if (__instance.DynamicVars.ContainsKey("DoomPower")) return;

            List<IHoverTip> list = __result as List<IHoverTip> ?? __result?.ToList() ?? new List<IHoverTip>();
            list.Add(HoverTipFactory.FromPower<DoomPower>());
            __result = list;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 灾厄悬停提示失败: {e}");
        }
    }
}
