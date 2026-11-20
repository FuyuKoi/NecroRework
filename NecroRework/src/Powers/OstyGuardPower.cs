using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Powers;

/// <summary>
/// 奥斯提守护（「吸引仇恨」）：持续期间取消 50% 分摊，回到原版的全量吸收。
/// 挂在奥斯提身上，敌方回合结束时减少 1 层，归零就消失。
/// </summary>
public sealed class OstyGuardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy) return;
        if (Owner is not { IsAlive: true }) return;

        await PowerCmd.Decrement(this);
    }
}
