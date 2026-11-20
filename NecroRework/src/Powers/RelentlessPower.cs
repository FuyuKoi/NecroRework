using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 紧追不放：挂在敌人身上的 debuff。
/// 本回合内，奥斯提每攻击这名敌人 2 次，就给予其 Amount 层易伤；敌方回合结束时自动移除。
/// </summary>
public sealed class RelentlessPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    private int _hits;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer, DamageResult result,
        ValueProp props, Creature target, CardModel cardSource)
    {
        if (target != Owner) return;
        if (dealer?.Monster is not Osty osty) return;

        var petOwner = osty.Creature.PetOwner;
        if (petOwner == null || petOwner.Creature != Applier) return;

        _hits++;
        if (_hits % 2 != 0) return;

        await PowerCmd.Apply<VulnerablePower>(choiceContext, Owner, Amount, Applier, null);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Remove(this);
        }
    }
}
