using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 此生渺渺：累计本回合打出的「虚无」牌，每满 6 张给 1 层无实体，然后重新计数。
/// </summary>
public sealed class FleetingLifePower : PowerModel
{
    private const int CardsPerIntangible = 6;

    private int _playedThisTurn;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>图标上的数字 = 本回合已经打出的虚无牌数量（0~5，到 6 就清零）。</summary>
    public override int DisplayAmount => _playedThisTurn % CardsPerIntangible;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner is not { IsAlive: true } owner) return;

        CardModel played = cardPlay.Card;
        if (played.Owner?.Creature != owner) return;
        if (!played.Keywords.Contains(CardKeyword.Ethereal)) return;

        // 打出「此生渺渺」自己那一下不算，否则它会白送一张进度
        if (played is Cards.FleetingLife) return;

        _playedThisTurn++;
        InvokeDisplayAmountChanged();

        if (_playedThisTurn % CardsPerIntangible != 0) return;

        Flash();
        await PowerCmd.Apply<IntangiblePower>(choiceContext, owner, 1m, Applier, null);
    }

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player) return Task.CompletedTask;
        if (Owner is not { IsAlive: true }) return Task.CompletedTask;

        _playedThisTurn = 0;
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }
}
