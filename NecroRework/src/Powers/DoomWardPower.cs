using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 灾厄免疫：免疫「下一次」获得的灾厄（来源不限，含自己给自己上的）。
/// 触发一次后消耗一层。
///
/// 实现照抄原版 ArtifactPower 的写法：在「将要获得多少层能力」这一步把数值改成 0，
/// 引擎就会认为这次施加发生了但数值为 0，我们再扣掉自己一层。
/// </summary>
public sealed class DoomWardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount, Creature applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;

        if (target != Owner) return false;
        if (canonicalPower is not DoomPower) return false;
        if (amount <= 0m) return false;

        modifiedAmount = 0m;
        return true;
    }

    public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        if (power is not DoomPower) return;

        Flash();
        await PowerCmd.Decrement(this);
    }
}
