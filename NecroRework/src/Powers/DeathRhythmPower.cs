using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 死亡律动：在本回合，你每打出一张牌就受到 2 点伤害。不可再额外叠加。
/// 固定 2 层、回合结束时消失；重复打出「透支」不会把它叠上去（TryModifyPowerAmountReceived 直接拒绝）。
/// </summary>
public sealed class DeathRhythmPower : PowerModel
{
    private CardModel _sourceCard;

    public override PowerType Type => PowerType.Debuff;

    /// <summary>Single 会让层数数字不显示——它本来就固定 2 层，没有可读的数字。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>已经有一层了就不再叠（重复打出「透支」不叠加）。</summary>
    public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount, Creature applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;

        if (target != Owner) return false;
        if (canonicalPower == null || canonicalPower.Id != Id) return false;
        if (amount <= 0m) return false;

        modifiedAmount = 0m;
        return true;
    }

    /// <summary>记下是哪张牌给的，这张牌自己那次出牌不吃伤害。</summary>
    public override Task BeforeApplied(Creature target, decimal amount, Creature applier, CardModel cardSource)
    {
        _sourceCard = cardSource;
        return Task.CompletedTask;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null || !Owner.IsAlive) return;

        if (_sourceCard != null && ReferenceEquals(cardPlay.Card, _sourceCard))
        {
            _sourceCard = null;
            return;
        }

        Flash();
        await CreatureCmd.Damage(choiceContext, Owner, Amount, ValueProp.Unpowered, null, null);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (participants == null || !participants.Contains(Owner)) return;

        await PowerCmd.Remove(this);
    }
}
