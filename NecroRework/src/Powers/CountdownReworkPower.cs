using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 倒数计时（重做）：只在自己的回合开始时触发——给随机敌人上灾厄，然后自己等量消灾厄。
/// 原版是「每个回合开始（含敌方回合）」，而且不会清自己的灾厄，所以整段换掉。
/// </summary>
public sealed class CountdownReworkPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (participants == null || !participants.Contains(Owner)) return;
        if (Owner is not { IsAlive: true }) return;

        var enemies = CombatState.HittableEnemies;
        if (enemies.Count == 0) return;

        Flash();
        var ctx = new ThrowingPlayerChoiceContext();

        var enemy = Owner.Player?.RunState.Rng.CombatTargets.NextItem(enemies);
        if (enemy != null)
            await PowerCmd.Apply<DoomPower>(ctx, enemy, Amount, Owner, null);

        // 打出去多少，自己就消掉多少（不够就清到 0，由原版加法自己夹）
        await DoomUtil.RemoveDoom(ctx, null, Owner, Amount);
    }
}
