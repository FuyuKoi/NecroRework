using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NecroRework;

/// <summary>
/// 把奥斯提的血条做成 75% 不透明度（只影响血条本体与血量数字，不碰名字与能力图标）。
/// 外层节点（NCreatureStateDisplay，%HealthBar）的 alpha 会被出场／退场补间反复改写，
/// 直接改它会被冲掉；所以改内层 NHealthBar 的 alpha，和父节点的动画相乘，淡入淡出照常。
/// </summary>
internal static class OstyHpBar
{
    private const float Alpha = 0.75f;

    public static void Install(Harmony harmony)
    {
        var ready = AccessTools.Method(typeof(NCreature), "_Ready");
        if (ready == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 NCreature._Ready，奥斯提血条透明度未生效");
            return;
        }

        harmony.Patch(ready, postfix: new HarmonyMethod(typeof(OstyHpBar), nameof(ReadyPostfix)));
    }

    public static void ReadyPostfix(NCreature __instance)
    {
        try
        {
            if (__instance.Entity?.Monster is not Osty) return;

            var stateDisplay = __instance.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
            var bar = stateDisplay?.GetNodeOrNull<NHealthBar>("%HealthBar");
            if (bar == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到奥斯提的 NHealthBar 节点，透明度未生效");
                return;
            }

            bar.Modulate = new Color(1f, 1f, 1f, Alpha);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 设置奥斯提血条透明度失败: {e}");
        }
    }
}
