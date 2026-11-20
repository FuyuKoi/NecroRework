using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 能力图标。
/// 1) 自制能力没有自己的图标资源（游戏按 ID 去 atlases/power_atlas 里找，肯定找不到），
///    会把能力图标显示成空白，这里把路径重定向到原版同效果的能力图标上。
/// 2) 「为你而死」换成一张叠了 #99FF99、15% 不透明的自定义图标（mod 目录下的 die_for_you_icon.png）。
/// </summary>
internal static class PowerIcons
{
    private const string DieForYouIconFile = "die_for_you_icon.png";

    private static Texture2D? _dieForYouIcon;
    private static bool _dieForYouIconTried;

    public static void Install(Harmony harmony)
    {
        var getter = AccessTools.PropertyGetter(typeof(PowerModel), "PackedIconPath");
        if (getter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 PowerModel.PackedIconPath，自制能力将没有图标");
        }
        else
        {
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(PowerIcons), nameof(PackedIconPathPostfix)));
        }

        var iconGetter = AccessTools.PropertyGetter(typeof(PowerModel), "Icon");
        if (iconGetter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 PowerModel.Icon，为你而死的自定义图标未生效");
        }
        else
        {
            harmony.Patch(iconGetter, postfix: new HarmonyMethod(typeof(PowerIcons), nameof(IconPostfix)));
        }

        var bigIconGetter = AccessTools.PropertyGetter(typeof(PowerModel), "BigIcon");
        if (bigIconGetter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 PowerModel.BigIcon，为你而死的大图标未替换");
        }
        else
        {
            harmony.Patch(bigIconGetter, postfix: new HarmonyMethod(typeof(PowerIcons), nameof(BigIconPostfix)));
        }
    }

    public static void PackedIconPathPostfix(object __instance, ref string __result)
    {
        try
        {
            switch (__instance)
            {
                case DoomWardPower:
                    __result = ModelDb.Power<ArtifactPower>().PackedIconPath;
                    break;
                case OstyGuardPower:
                    __result = ModelDb.Power<DieForYouPower>().PackedIconPath;
                    break;
                case SpilloverPower:
                    __result = ModelDb.Power<DoomPower>().PackedIconPath;
                    break;
                case EnergyCarryPower:
                    __result = ModelDb.Power<EnergyNextTurnPower>().PackedIconPath;
                    break;
                case PullFromBelowPower:
                    __result = ModelDb.Power<HauntPower>().PackedIconPath;
                    break;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 重定向能力图标失败: {e}");
        }
    }

    public static void IconPostfix(object __instance, ref Texture2D __result)
    {
        if (__instance is not DieForYouPower) return;

        var icon = LoadDieForYouIcon();
        if (icon != null)
            __result = icon;
    }

    public static void BigIconPostfix(object __instance, ref Texture2D __result)
    {
        if (__instance is not DieForYouPower) return;

        var icon = LoadDieForYouIcon();
        if (icon != null)
            __result = icon;
    }

    /// <summary>懒加载 mod 目录下的自定义图标；读不到就沿用原版图标。</summary>
    private static Texture2D? LoadDieForYouIcon()
    {
        if (_dieForYouIconTried) return _dieForYouIcon;
        _dieForYouIconTried = true;

        try
        {
            var path = Path.Combine(Main.ModDirectory, DieForYouIconFile);
            if (!File.Exists(path))
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {path}，为你而死继续用原版图标");
                return null;
            }

            var image = Image.LoadFromFile(path);
            if (image == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 读取 {path} 失败，为你而死继续用原版图标");
                return null;
            }

            _dieForYouIcon = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 加载为你而死图标失败: {e}");
        }

        return _dieForYouIcon;
    }
}
