using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace NecroRework.Powers;

/// <summary>
/// 吞噬生命（重做）：每回合每打出 2 张「灵魂」，获得「层数」点费用。
/// 重复打出这张能力会叠层 → 每次触发给更多的费用。
/// </summary>
public sealed class DevourLifeReworkPower : PowerModel
{
    private const int SoulsPerTrigger = 2;

    private int _soulsThisTurn;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner is not { IsAlive: true } owner) return;
        if (Owner.Player is not { } player) return;

        if (cardPlay.Card is not Soul soul) return;
        if (soul.Owner?.Creature != owner) return;

        _soulsThisTurn++;
        if (_soulsThisTurn % SoulsPerTrigger != 0) return;

        Flash();
        await PlayerCmd.GainEnergy(Amount, player);
    }

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player) return Task.CompletedTask;

        _soulsThisTurn = 0;
        return Task.CompletedTask;
    }
}
