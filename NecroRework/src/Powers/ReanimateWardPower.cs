using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 死者苏生的护佑：本回合内不会受到任何伤害（含非攻击伤害），也不会死于灾厄。
/// 等到下个自己回合开始时才移除，这样「回合结束时的灾厄判定」一定被挡住。
/// </summary>
public sealed class ReanimateWardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>所有掉血（攻击、非攻击、生命流失）都走这里。</summary>
    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        return target == Owner ? 0m : amount;
    }

    /// <summary>连「处死」也挡住（灾厄致死走 CreatureCmd.Kill）。</summary>
    public override bool ShouldDie(Creature creature)
    {
        return creature != Owner;
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (!participants.Contains(Owner)) return;

        await PowerCmd.Remove(this);
    }
}
