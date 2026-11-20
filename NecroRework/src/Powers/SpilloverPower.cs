using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 殃及（「吸引仇恨」的副作用，挂在玩家身上）：
/// 在「吸引仇恨」生效期间，奥斯提每失去 2 点生命值，玩家就获得 1 层灾厄。
/// 与「吸引仇恨」同寿：敌方回合结束时一起减 1 层。
///
/// 灾厄以「玩家自己作为施加者」的方式施加，所以会正常触发死亡之门 / 厄运之衣，
/// 并且因为是给自己上灾厄，不会触发血肉戏法。
/// </summary>
public sealed class SpilloverPower : PowerModel
{
    private const decimal HpPerDoom = 2m;

    private decimal _hpBuffer;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (delta >= 0m) return;
        if (Owner is not { IsAlive: true } me) return;
        if (creature.Monster is not Osty || creature.PetOwner != me.Player) return;
        if (!creature.HasPower<OstyGuardPower>()) return;

        _hpBuffer -= delta;
        int times = 0;
        while (_hpBuffer >= HpPerDoom)
        {
            _hpBuffer -= HpPerDoom;
            times++;
        }

        if (times <= 0) return;

        await PowerCmd.Apply<DoomPower>(new ThrowingPlayerChoiceContext(), me, times, me, null);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy) return;
        if (Owner is not { IsAlive: true }) return;

        await PowerCmd.Decrement(this);
    }
}
