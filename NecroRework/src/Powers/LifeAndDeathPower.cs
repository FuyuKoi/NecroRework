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
    /// <summary>
    /// 已经答应「替骨妹承担」但还没真正转移过去的灾厄层数。
    /// 用累加而不是覆盖：同一次施加流程里可能被调用多次（先 Given 后 Received、
    /// 或者精神过载这类连着施加两发），覆盖会把前一笔吞掉，导致奥斯提不掉血。
    /// </summary>
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

        int absorb = (int)Math.Min(Amount - _pendingAbsorb, amount);
        if (absorb <= 0) return false;

        _pendingAbsorb += absorb;
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
        int before = (int)DoomUtil.DoomOf(osty);
        await PowerCmd.Apply<DoomPower>(choiceContext, osty, absorb, Owner, null);
        int after = (int)DoomUtil.DoomOf(osty);

        // 按「真正加上去的层数」结算伤害：奥斯提已经满灾厄、或者被别的能力挡掉时不会多算，
        // 反过来也不会因为层数被吞掉而不掉血。
        int gained = Math.Max(0, after - before);
        Main.Log.Info($"[{Main.ModId}] 生与死：奥斯提承担 {absorb} 层灾厄（{before}→{after}），应受 {gained} 点伤害");

        if (gained > 0)
        {
            // 「失去等量生命值」而不是「受到伤害」：
            // Unblockable（不吃格挡、不是被打）、Unpowered（力量/易伤/荆棘/反射都不参与），
            // 但依旧走完整伤害管线，所以数字、死亡判定、厄运之衣这些都正常。
            await CreatureCmd.Damage(choiceContext, osty, gained, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
            Main.Log.Info($"[{Main.ModId}] 生与死：奥斯提失去 {gained} 点生命值，剩余生命 {osty.CurrentHp}");
        }

        await PowerCmd.ModifyAmount(choiceContext, this, -absorb, Owner, null);
    }
}
