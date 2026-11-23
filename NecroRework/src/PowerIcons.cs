using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
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
    private const string DeathRhythmIconFile = "death_rhythm.png";

    private static Texture2D? _dieForYouIcon;
    private static bool _dieForYouIconTried;
    private static Texture2D? _deathRhythmIcon;
    private static bool _deathRhythmIconTried;

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

        InstallNodeFallback(harmony);
    }

    /// <summary>
    /// 节点层兜底：有的 mod（RitsuLib / BaseLib / Act4Heart）也补了
    /// PowerModel.PackedIconPath / Icon，会把我们的前缀短路掉。
    /// 所以在能力图标节点 NPower.Reload 之后直接把贴图塞进 _icon / _powerFlash。
    /// </summary>
    private static void InstallNodeFallback(Harmony harmony)
    {
        try
        {
            var reload = AccessTools.Method(typeof(NPower), "Reload", Type.EmptyTypes);
            if (reload == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 NPower.Reload，能力图标只有模型层重定向");
                return;
            }

            harmony.Patch(reload, postfix: new HarmonyMethod(typeof(PowerIcons), nameof(ReloadPostfix)));
            Main.Log.Info($"[{Main.ModId}] 能力图标节点层兜底已挂载");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 能力图标节点层兜底挂载失败: {e}");
        }
    }

    private static AccessTools.FieldRef<NPower, TextureRect>? _iconField;
    private static AccessTools.FieldRef<NPower, CpuParticles2D>? _flashField;
    private static bool _reloadErrorLogged;

    private static void ReloadPostfix(NPower __instance)
    {
        try
        {
            var model = __instance.Model;
            if (model == null) return;

            var icon = ResolveIcon(model);
            if (icon == null) return;

            _iconField ??= AccessTools.FieldRefAccess<NPower, TextureRect>("_icon");
            var rect = _iconField(__instance);
            if (rect != null && rect.Texture != icon) rect.Texture = icon;

            var big = ResolveBigIcon(model);
            if (big == null) return;

            _flashField ??= AccessTools.FieldRefAccess<NPower, CpuParticles2D>("_powerFlash");
            var flash = _flashField(__instance);
            if (flash != null && flash.Texture != big) flash.Texture = big;
        }
        catch (Exception e)
        {
            if (_reloadErrorLogged) return;
            _reloadErrorLogged = true;
            Main.Log.Error($"[{Main.ModId}] 能力图标节点层兜底失败（后续不再记录）: {e}");
        }
    }

    /// <summary>自制能力 → 借用的原版能力实例；不是我们的能力就返回 null。</summary>
    private static PowerModel? VanillaPowerFor(PowerModel power) => power switch
    {
        DoomWardPower => ModelDb.Power<ArtifactPower>(),
        OstyGuardPower => ModelDb.Power<DieForYouPower>(),
        SpilloverPower => ModelDb.Power<DoomPower>(),
        EnergyCarryPower => ModelDb.Power<EnergyNextTurnPower>(),
        PullFromBelowPower => ModelDb.Power<HauntPower>(),
        RelentlessPower => ModelDb.Power<SicEmPower>(),
        CountdownReworkPower => ModelDb.Power<CountdownPower>(),
        GhostLanternPower => ModelDb.Power<SpiritOfAshPower>(),
        CaptureSpiritStrengthDownPower => ModelDb.Power<MonarchsGazeStrengthDownPower>(),
        SharedFatePower => ModelDb.Power<StrengthPower>(),
        BeginnerMindPower => ModelDb.Power<CallOfTheVoidPower>(),
        DevourLifeReworkPower => ModelDb.Power<DevourLifePower>(),
        SpiritOfAshReworkPower => ModelDb.Power<SpiritOfAshPower>(),
        FleetingLifePower => ModelDb.Power<IntangiblePower>(),
        NecroMasteryGuardPower => ModelDb.Power<NecroMasteryPower>(),
        ReanimateWardPower => ModelDb.Power<IntangiblePower>(),
        SoulGatherPower => ModelDb.Power<ChainsOfBindingPower>(),
        RiverOfOblivionPower => ModelDb.Power<IntangiblePower>(),
        WorldBurdenPower => ModelDb.Power<DieForYouPower>(),
        DeathRhythmPower => null,
        _ => null,
    };

    private static Texture2D? ResolveIcon(PowerModel power)
    {
        switch (power)
        {
            case DieForYouPower:
                return LoadDieForYouIcon();
            case DeathRhythmPower:
                return LoadCustomIcon(DeathRhythmIconFile, ref _deathRhythmIcon, ref _deathRhythmIconTried, "死亡律动");
            case SicEmCounterPower:
                return ModelDb.Relic<FakeSneckoEye>().Icon;
        }

        return VanillaPowerFor(power)?.Icon;
    }

    private static Texture2D? ResolveBigIcon(PowerModel power)
    {
        switch (power)
        {
            case DieForYouPower:
                return LoadDieForYouIcon();
            case DeathRhythmPower:
                return LoadCustomIcon(DeathRhythmIconFile, ref _deathRhythmIcon, ref _deathRhythmIconTried, "死亡律动");
            case SicEmCounterPower:
                return ModelDb.Relic<FakeSneckoEye>().BigIcon;
        }

        return VanillaPowerFor(power)?.BigIcon;
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
                case RelentlessPower:
                    // 用原版「紧追不放」能力（SicEmPower）的图标
                    __result = ModelDb.Power<SicEmPower>().PackedIconPath;
                    break;
                case SicEmCounterPower:
                    // 用遗物「异蛇之眼？？？」的问号当计数器图标
                    __result = ModelDb.Relic<FakeSneckoEye>().PackedIconPath;
                    break;
                case CountdownReworkPower:
                    __result = ModelDb.Power<CountdownPower>().PackedIconPath;
                    break;
                case GhostLanternPower:
                    __result = ModelDb.Power<SpiritOfAshPower>().PackedIconPath;
                    break;
                case CaptureSpiritStrengthDownPower:
                    // 沿用原版「临时减力量」那个能力的图标
                    __result = ModelDb.Power<MonarchsGazeStrengthDownPower>().PackedIconPath;
                    break;
                case SharedFatePower:
                    // 命运同担同步的是力量，直接用「力量」的图标
                    __result = ModelDb.Power<StrengthPower>().PackedIconPath;
                    break;
                case BeginnerMindPower:
                    // 初心借用「灾厄之种」这类回合开始型能力的外观，用原版「预谋」图标
                    __result = ModelDb.Power<CallOfTheVoidPower>().PackedIconPath;
                    break;
                case DevourLifeReworkPower:
                    __result = ModelDb.Power<DevourLifePower>().PackedIconPath;
                    break;
                case SpiritOfAshReworkPower:
                    __result = ModelDb.Power<SpiritOfAshPower>().PackedIconPath;
                    break;
                case FleetingLifePower:
                    // 此生渺渺给的是无实体，借用「无实体」的图标
                    __result = ModelDb.Power<IntangiblePower>().PackedIconPath;
                    break;
                case NecroMasteryGuardPower:
                    // 亡灵精通的附赠能力，和本体共用图标
                    __result = ModelDb.Power<NecroMasteryPower>().PackedIconPath;
                    break;

                case ReanimateWardPower:
                    // 死者苏生的免伤护佑，借用「无实体」图标
                    __result = ModelDb.Power<IntangiblePower>().PackedIconPath;
                    break;

                case SoulGatherPower:
                    // 聚魂术：借用「缚魂」的图标
                    __result = ModelDb.Power<ChainsOfBindingPower>().PackedIconPath;
                    break;

                case RiverOfOblivionPower:
                    // 相忘于川：借用「无实体」图标
                    __result = ModelDb.Power<IntangiblePower>().PackedIconPath;
                    break;

                case WorldBurdenPower:
                    // 负世：和「为你而死」共用图标
                    __result = ModelDb.Power<DieForYouPower>().PackedIconPath;
                    break;

                case TurnToDustPower:
                    // 化作尘泥：借「壁垒」的图标（都跟格挡有关）
                    __result = ModelDb.Power<BarricadePower>().PackedIconPath;
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
        switch (__instance)
        {
            case DieForYouPower:
            {
                var icon = LoadDieForYouIcon();
                if (icon != null) __result = icon;
                break;
            }
            case DeathRhythmPower:
            {
                var icon = LoadCustomIcon(DeathRhythmIconFile, ref _deathRhythmIcon, ref _deathRhythmIconTried, "死亡律动");
                if (icon != null) __result = icon;
                break;
            }
        }
    }

    public static void BigIconPostfix(object __instance, ref Texture2D __result)
    {
        switch (__instance)
        {
            case DieForYouPower:
            {
                var icon = LoadDieForYouIcon();
                if (icon != null) __result = icon;
                break;
            }
            case DeathRhythmPower:
            {
                var icon = LoadCustomIcon(DeathRhythmIconFile, ref _deathRhythmIcon, ref _deathRhythmIconTried, "死亡律动");
                if (icon != null) __result = icon;
                break;
            }
        }
    }

    /// <summary>懒加载 mod 目录下的自定义图标；读不到就沿用原版图标。</summary>
    private static Texture2D? LoadDieForYouIcon()
    {
        return LoadCustomIcon(DieForYouIconFile, ref _dieForYouIcon, ref _dieForYouIconTried, "为你而死");
    }

    private static Texture2D? LoadCustomIcon(string fileName, ref Texture2D? cache, ref bool tried, string what)
    {
        if (tried) return cache;
        tried = true;

        try
        {
            var path = Path.Combine(Main.ModDirectory, fileName);
            if (!File.Exists(path))
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {path}，{what}继续用原版图标");
                return null;
            }

            var image = Image.LoadFromFile(path);
            if (image == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 读取 {path} 失败，{what}继续用原版图标");
                return null;
            }

            cache = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 加载{what}图标失败: {e}");
        }

        return cache;
    }
}
