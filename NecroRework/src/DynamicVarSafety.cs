using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework;

/// <summary>
/// 动态变量保险丝。
///
/// 引擎里 <c>CalculatedVar</c> 会通过 <c>DynamicVars.CalculationBase</c> /
/// <c>CalculationExtra</c>（伤害类是 <c>ExtraDamage</c>）去取配对的变量，
/// 而 <c>DynamicVarSet</c> 的访问器是 <c>_vars["键"]</c> —— 键不存在就直接抛
/// <c>KeyNotFoundException</c>。这个异常发生在 <c>CardModel.ToMutable()</c> 里，
/// 于是「奖励里抽到这张牌 / 事件给牌 / 变形」这些路径会整条任务链爆掉，
/// 表现为卡死或者直接崩溃（例如：水晶球事件奖励抽到「增生」）。
///
/// 这类错配是 mod 作者写卡时很容易犯的（只要 Vars 里漏了配对变量就会中），
/// 而且只在特定流程里才会踩到，很难测试覆盖。所以这里给三个访问器加兜底：
/// 缺键时返回一个 0 值变量，效果是「数字显示 0」，而不是整局崩掉。
/// </summary>
internal static class DynamicVarSafety
{
    private static readonly AccessTools.FieldRef<DynamicVarSet, Dictionary<string, DynamicVar>> VarsRef =
        AccessTools.FieldRefAccess<DynamicVarSet, Dictionary<string, DynamicVar>>("_vars");

    public static void Install(Harmony harmony)
    {
        int patched = 0;
        patched += Patch(harmony, nameof(DynamicVarSet.CalculationBase), nameof(BasePrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.CalculationExtra), nameof(ExtraPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.ExtraDamage), nameof(ExtraDamagePrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Energy), nameof(EnergyPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Cards), nameof(CardsPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Block), nameof(BlockPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Summon), nameof(SummonPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Damage), nameof(DamagePrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.Heal), nameof(HealPrefix));
        patched += Patch(harmony, nameof(DynamicVarSet.OstyDamage), nameof(OstyDamagePrefix));

        Main.Log.Info($"[{Main.ModId}] 动态变量保险丝：已挂载 {patched}/10（缺 CalculationBase / CalculationExtra / ExtraDamage / Energy / Cards / Block / Summon / Damage / Heal / OstyDamage 时返回 0，不再抛异常）");
    }

    private static int Patch(Harmony harmony, string property, string prefix)
    {
        try
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(DynamicVarSet), property);
            if (getter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 动态变量保险丝：找不到 DynamicVarSet.{property}");
                return 0;
            }

            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(DynamicVarSafety), prefix));
            return 1;
        }
        catch (System.Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 动态变量保险丝：挂 {property} 失败 {e.Message}");
            return 0;
        }
    }

    private static bool BasePrefix(DynamicVarSet __instance, ref CalculationBaseVar __result)
    {
        return TryFallback(__instance, "CalculationBase", () => new CalculationBaseVar(0m), ref __result);
    }

    private static bool ExtraPrefix(DynamicVarSet __instance, ref CalculationExtraVar __result)
    {
        return TryFallback(__instance, "CalculationExtra", () => new CalculationExtraVar(1m), ref __result);
    }

    private static bool ExtraDamagePrefix(DynamicVarSet __instance, ref ExtraDamageVar __result)
    {
        return TryFallback(__instance, "ExtraDamage", () => new ExtraDamageVar(0m), ref __result);
    }

    private static bool EnergyPrefix(DynamicVarSet __instance, ref EnergyVar __result) =>
        TryFallback(__instance, "Energy", () => new EnergyVar(0), ref __result);

    private static bool CardsPrefix(DynamicVarSet __instance, ref CardsVar __result) =>
        TryFallback(__instance, "Cards", () => new CardsVar(0), ref __result);

    private static bool BlockPrefix(DynamicVarSet __instance, ref BlockVar __result) =>
        TryFallback(__instance, "Block", () => new BlockVar(0m, ValueProp.Unpowered), ref __result);

    private static bool SummonPrefix(DynamicVarSet __instance, ref SummonVar __result) =>
        TryFallback(__instance, "Summon", () => new SummonVar(0m), ref __result);

    private static bool DamagePrefix(DynamicVarSet __instance, ref DamageVar __result) =>
        TryFallback(__instance, "Damage", () => new DamageVar(0m, ValueProp.Move), ref __result);

    private static bool HealPrefix(DynamicVarSet __instance, ref HealVar __result) =>
        TryFallback(__instance, "Heal", () => new HealVar(0m), ref __result);

    private static bool OstyDamagePrefix(DynamicVarSet __instance, ref OstyDamageVar __result) =>
        TryFallback(__instance, "OstyDamage", () => new OstyDamageVar(0m, ValueProp.Move), ref __result);

    private static bool TryFallback<T>(DynamicVarSet set, string key, System.Func<T> factory, ref T result) where T : DynamicVar
    {
        try
        {
            Dictionary<string, DynamicVar>? vars = VarsRef(set);
            if (vars != null && vars.ContainsKey(key)) return true;   // 正常情况：走原版

            result = factory();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
