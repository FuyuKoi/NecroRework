using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 命运同担：除原有的「你与目标各失去若干力量」外，打出后获得一层
/// <see cref="SharedFatePower"/>，本场战斗中任何敌人获得力量时奥斯提也获得等量力量。
/// </summary>
internal static class SharedFateRework
{
    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Hook), "AfterPowerAmountChanged");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterPowerAmountChanged，命运同担的同步力量不会生效");
            return;
        }

        harmony.Patch(target,
            postfix: new HarmonyMethod(typeof(SharedFateRework), nameof(AfterPowerAmountChangedPostfix)));
    }

    private static void AfterPowerAmountChangedPostfix(PowerModel power, decimal amount)
    {
        try
        {
            if (power is not StrengthPower || amount <= 0m) return;

            Creature? enemy = power.Owner;
            if (enemy == null || !enemy.IsEnemy) return;   // 只关心敌人涨力量

            ICombatState? state = enemy.CombatState;
            if (state == null) return;

            foreach (Player player in state.Players)
            {
                Creature? creature = player.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.GetPower<SharedFatePower>() == null) continue;

                Creature? osty = player.Osty;
                if (osty is not { IsAlive: true }) continue;

                _ = MirrorAsync(creature, osty, amount);
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 命运同担同步力量失败: {e}");
        }
    }

    private static async Task MirrorAsync(Creature source, Creature osty, decimal amount)
    {
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), osty, amount, source, null);
    }
}
