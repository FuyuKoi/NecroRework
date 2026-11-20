using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework;

/// <summary>
/// 骨笛重做：每当奥斯提攻击 → 每回合奥斯提第一次攻击时，获得 5 点格挡。
///
/// 原版实现在 <c>BoneFlute.AfterAttack</c> 里，每次奥斯提攻击都给 BlockVar(2) 的格挡。
/// 这里做两件事：
/// 1. CanonicalVars 的格挡值 2 → 5（卡面 {Block} 占位符也会跟着变）；
/// 2. AfterAttack 前挂一个 prefix：本回合已经触发过就直接跳过原版实现，否则放行让原版发格挡。
/// 每回合开始时清空「本回合已触发」的记录。
/// </summary>
internal static class BoneFluteRework
{
    private const decimal BlockAmount = 5m;

    /// <summary>本回合已经触发过骨笛的遗物实例（一般是每场战斗一个可变副本）。</summary>
    private static readonly HashSet<BoneFlute> FiredThisTurn = new();

    public static void Install(Harmony harmony)
    {
        try
        {
            var varsGetter = AccessTools.PropertyGetter(typeof(BoneFlute), "CanonicalVars");
            if (varsGetter != null)
                harmony.Patch(varsGetter, postfix: new HarmonyMethod(typeof(BoneFluteRework), nameof(VarsPostfix)));
            else
                Main.Log.Warn($"[{Main.ModId}] 找不到 BoneFlute.CanonicalVars，骨笛数值未改");

            var afterAttack = AccessTools.Method(typeof(BoneFlute), "AfterAttack");
            if (afterAttack != null)
                harmony.Patch(afterAttack, prefix: new HarmonyMethod(typeof(BoneFluteRework), nameof(AfterAttackPrefix)));
            else
                Main.Log.Warn($"[{Main.ModId}] 找不到 BoneFlute.AfterAttack，骨笛「每回合一次」未生效");

            var turnStart = AccessTools.Method(typeof(AbstractModel), nameof(AbstractModel.AfterPlayerTurnStart));
            if (turnStart != null)
                harmony.Patch(turnStart, postfix: new HarmonyMethod(typeof(BoneFluteRework), nameof(TurnStartPostfix)));
            else
                Main.Log.Warn($"[{Main.ModId}] 找不到 AbstractModel.AfterPlayerTurnStart，骨笛计数无法按回合重置");

            Main.Log.Info($"[{Main.ModId}] 骨笛已重做：每回合奥斯提第一次攻击时获得 {BlockAmount} 点格挡");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 骨笛重做挂载失败: {e}");
        }
    }

    private static void VarsPostfix(ref IEnumerable<DynamicVar> __result)
    {
        __result = new DynamicVar[] { new BlockVar(BlockAmount, ValueProp.Unpowered) };
    }

    /// <summary>返回 false = 跳过原版 AfterAttack（本回合已经给过格挡了）。</summary>
    private static bool AfterAttackPrefix(BoneFlute __instance, AttackCommand command)
    {
        try
        {
            if (command.Attacker?.Monster is not Osty) return true;
            if (command.Attacker.PetOwner?.Creature != __instance.Owner.Creature) return true;

            // Add 返回 true 表示本回合第一次 → 放行原版（发 5 点格挡）
            return FiredThisTurn.Add(__instance);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 骨笛触发判断失败: {e}");
            return true;
        }
    }

    private static void TurnStartPostfix(AbstractModel __instance, Player player)
    {
        if (__instance is not BoneFlute flute) return;
        if (flute.Owner != player) return;

        FiredThisTurn.Remove(flute);
    }
}
