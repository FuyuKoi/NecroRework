using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 把奥斯提的血条做成 75% 不透明度（只影响血条本体与血量数字，不碰名字与能力图标）。
/// 外层节点（NCreatureStateDisplay，%HealthBar）的 alpha 会被出场／退场补间反复改写，
/// 直接改它会被冲掉；所以改内层 NHealthBar 的 alpha，和父节点的动画相乘，淡入淡出照常。
///
/// 「吸引仇恨」期间（奥斯提身上有 <see cref="OstyGuardPower"/>）透明度恢复 100%、亮度提到 1.5 倍，
/// 让玩家一眼看出这段伤害是奥斯提在扛。
/// </summary>
internal static class OstyHpBar
{
    private const float Alpha = 0.75f;

    private static readonly Color NormalTint = new(1f, 1f, 1f, Alpha);

    private static readonly Color GuardTint = new(1.5f, 1.5f, 1.5f, 1f);

    private static readonly AccessTools.FieldRef<NHealthBar, Creature> CreatureOf =
        AccessTools.FieldRefAccess<NHealthBar, Creature>("_creature");

    private static readonly AccessTools.FieldRef<NCreatureStateDisplay, NHealthBar> HealthBarOf =
        AccessTools.FieldRefAccess<NCreatureStateDisplay, NHealthBar>("_healthBar");

    public static void Install(Harmony harmony)
    {
        var ready = AccessTools.Method(typeof(NCreature), "_Ready");
        if (ready == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 NCreature._Ready，奥斯提血条透明度未生效");
            return;
        }

        harmony.Patch(ready, postfix: new HarmonyMethod(typeof(OstyHpBar), nameof(ReadyPostfix)));

        // 血量 / 格挡刷新时重新上色
        var refresh = AccessTools.Method(typeof(NHealthBar), nameof(NHealthBar.RefreshValues));
        if (refresh != null)
        {
            harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(OstyHpBar), nameof(RefreshPostfix)));
        }

        // 能力增删时立刻刷新一次，这样打出「吸引仇恨」当场就会变亮
        var applied = AccessTools.Method(typeof(NPowerContainer), "OnPowerApplied");
        var removed = AccessTools.Method(typeof(NPowerContainer), "OnPowerRemoved");
        if (applied != null && removed != null)
        {
            harmony.Patch(applied, postfix: new HarmonyMethod(typeof(OstyHpBar), nameof(PowerChangedPostfix)));
            harmony.Patch(removed, postfix: new HarmonyMethod(typeof(OstyHpBar), nameof(PowerChangedPostfix)));
        }
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

            Apply(bar);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 设置奥斯提血条透明度失败: {e}");
        }
    }

    public static void RefreshPostfix(NHealthBar __instance)
    {
        try
        {
            Apply(__instance);
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 刷新奥斯提血条亮度失败: {e}");
        }
    }

    public static void PowerChangedPostfix(NPowerContainer __instance)
    {
        try
        {
            var parent = __instance.GetParent();
            if (parent is not NCreatureStateDisplay display) return;

            var bar = HealthBarOf(display);
            if (bar == null) return;

            Apply(bar);
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 能力变化后刷新奥斯提血条失败: {e}");
        }
    }

    private static void Apply(NHealthBar bar)
    {
        var creature = CreatureOf(bar);
        if (creature?.Monster is not Osty) return;

        bool guarding = creature.HasPower<OstyGuardPower>();
        bar.Modulate = guarding ? GuardTint : NormalTint;
    }
}
