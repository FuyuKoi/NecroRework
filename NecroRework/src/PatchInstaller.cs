using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework;

/// <summary>
/// 按 Reworks 表里的卡牌类型，动态挂 Harmony 补丁。
/// 用 __args 传参，避免依赖游戏内部参数名（版本更新时更抗性）。
/// </summary>
internal static class PatchInstaller
{
    public static void Install(Harmony harmony)
    {
        var varsPostfix = new HarmonyMethod(typeof(Patches), nameof(Patches.VarsPostfix));
        var playPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.PlayPrefix));

        foreach (var type in Reworks.Vars.Keys)
        {
            var getter = AccessTools.PropertyGetter(type, "CanonicalVars");
            if (getter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.CanonicalVars，跳过数值改写");
                continue;
            }
            harmony.Patch(getter, postfix: varsPostfix);
        }

        foreach (var type in Reworks.Play.Keys)
        {
            var onPlay = AccessTools.Method(type, "OnPlay");
            if (onPlay == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.OnPlay，跳过效果改写");
                continue;
            }
            harmony.Patch(onPlay, prefix: playPrefix);
        }

        Main.Log.Info($"[{Main.ModId}] 已挂载 {Reworks.Vars.Count} 个数值改写 / {Reworks.Play.Count} 个效果改写");
    }
}

internal static class Patches
{
    public static void VarsPostfix(object __instance, ref IEnumerable<DynamicVar> __result)
    {
        if (__instance is not CardModel card) return;
        if (!Reworks.Vars.TryGetValue(card.GetType(), out var factory)) return;

        try
        {
            __result = factory(card);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 数值改写失败: {e}");
        }
    }

    public static bool PlayPrefix(object __instance, ref Task __result, object[] __args)
    {
        if (__instance is not CardModel card) return true;
        if (!Reworks.Play.TryGetValue(card.GetType(), out var impl)) return true;

        try
        {
            var ctx = (MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext)__args[0];
            var play = (CardPlay)__args[1];
            __result = impl(card, ctx, play);
            return false; // 跳过原版实现
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 效果改写失败: {e}");
            return true;
        }
    }
}
