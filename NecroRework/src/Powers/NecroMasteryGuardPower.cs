using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 亡灵精通的附赠：每当奥斯提失去生命值，把等量数值攒起来，
/// 在你下个回合开始时一次性变成格挡。
/// </summary>
public sealed class NecroMasteryGuardPower : PowerModel
{
    private decimal _pendingBlock;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>图标上显示「已经攒了多少格挡」。</summary>
    public override int DisplayAmount => (int)_pendingBlock;

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (delta >= 0m) return;
        if (creature.Monster is not Osty) return;
        if (creature.PetOwner != Owner?.Player) return;

        _pendingBlock += -delta * Amount;
        InvokeDisplayAmountChanged();
        await Task.CompletedTask;
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (Owner is not { IsAlive: true } me) return;

        decimal pending = _pendingBlock;
        _pendingBlock = 0m;
        InvokeDisplayAmountChanged();

        if (pending <= 0m) return;

        await CreatureCmd.GainBlock(me, pending, ValueProp.Move, null);
    }
}
