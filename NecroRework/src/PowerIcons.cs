using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 自制能力没有自己的图标资源（游戏按 ID 去 atlases/power_atlas 里找，肯定找不到），
/// 会把能力图标显示成空白。这里把图标路径重定向到原版同效果的能力图标上。
/// </summary>
internal static class PowerIcons
{
    public static void Install(Harmony harmony)
    {
        var getter = AccessTools.PropertyGetter(typeof(PowerModel), "PackedIconPath");
        if (getter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 PowerModel.PackedIconPath，自制能力将没有图标");
            return;
        }

        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(PowerIcons), nameof(PackedIconPathPostfix)));
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
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 重定向能力图标失败: {e}");
        }
    }
}
