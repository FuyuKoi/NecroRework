using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 钙化（重做）追加的「奥斯提每次攻击后给你格挡」。
/// 原版 CalcifyPower 只做伤害加成，改不了它的方法体，
/// 所以在 Hook.AfterAttack 上挂一拍：奥斯提替本玩家打了几段，就起几份格挡。
/// </summary>
internal static class CalcifyGuard
{
    /// <summary>每个玩家当前每段攻击的格挡量（随钙化层数累加，每场战斗独立）。</summary>
    private static readonly ConditionalWeakTable<Creature, StrongBox<decimal>> Blocks = new();

    public static void Add(Creature player, decimal block)
    {
        if (player == null || block <= 0m) return;

        var box = Blocks.GetOrCreateValue(player);
        box.Value += block;
    }

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Hook), "AfterAttack",
            [typeof(ICombatState), typeof(PlayerChoiceContext), typeof(AttackCommand)]);
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterAttack，钙化格挡不会生效");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(CalcifyGuard), nameof(AfterAttackPostfix)));
    }

    private static void AfterAttackPostfix(AttackCommand command)
    {
        if (!NecroSettings.CardReworks) return;

        // Harmony 不会 await 返回值，这里自己起一个任务，异常自己吃掉
        _ = GrantAsync(command);
    }

    private static async Task GrantAsync(AttackCommand command)
    {
        try
        {
            if (command?.Attacker is not { Monster: Osty } osty) return;

            var owner = osty.PetOwner?.Creature;
            if (owner is not { IsAlive: true }) return;
            if (!owner.HasPower<CalcifyPower>()) return;
            if (!Blocks.TryGetValue(owner, out var box) || box.Value <= 0m) return;

            int hits = command.Results?.Count() ?? 0;
            if (hits <= 0) return;

            await CreatureCmd.GainBlock(owner, box.Value * hits, ValueProp.Unpowered, null);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 钙化格挡结算异常: {e}");
        }
    }
}
