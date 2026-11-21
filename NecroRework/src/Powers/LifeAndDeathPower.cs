using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 生与死：接下来你获得的 N 层灾厄由奥斯提承担（算作你施加给他），
/// 而他在承受这些灾厄时，会受到等量伤害。
/// 挡住多少层就消耗多少层，用完自动消失。
/// </summary>
public sealed class LifeAndDeathPower : PowerModel
{
    private int _pendingAbsorb;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount, Creature applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;

        if (canonicalPower is not DoomPower) return false;
        if (target != Owner) return false;
        if (amount <= 0m) return false;

        // 奥斯提不在场（死亡 / 未召唤）时，这一层不该吞掉灾厄：
        // 不接管，骨妹照常获得灾厄，本能力层数也不减少。
        if (Owner.Player?.Osty is not { IsAlive: true }) return false;

        int absorb = (int)Math.Min(Amount, amount);
        if (absorb <= 0) return false;

        _pendingAbsorb = absorb;
        modifiedAmount = amount - absorb;
        return true;
    }

    public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        if (power is not DoomPower) return;

        int absorb = _pendingAbsorb;
        _pendingAbsorb = 0;
        if (absorb <= 0) return;

        if (Owner.Player?.Osty is not { IsAlive: true } osty) return;

        var choiceContext = new ThrowingPlayerChoiceContext();

        // 灾厄算作「骨妹施加给奥斯提」（触发死亡之门 / 厄运之衣，不触发血肉戏法）。
        await PowerCmd.Apply<DoomPower>(choiceContext, osty, absorb, Owner, null);

        // 他承受这些灾厄时，受到等量伤害。
        await CreatureCmd.Damage(choiceContext, osty, absorb, ValueProp.Unpowered | ValueProp.Move, null, null);

        await PowerCmd.ModifyAmount(choiceContext, this, -absorb, Owner, null);
    }
}
