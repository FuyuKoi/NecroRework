using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Powers;

/// <summary>
/// 化作尘泥：常驻 buff。每个回合开始前（引擎清空格挡之前的那个时点），
/// 把当时剩余的格挡值的一半（向下取整）转化为等量的召唤。
/// 挂的是 <c>Hook.BeforeSideTurnStart</c>：它比清空格挡更早触发，
/// 所以这时候读到的 <see cref="Creature.Block"/> 正是「活过敌方回合之后剩下的格挡」。
/// </summary>
public sealed class TurnToDustPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (!participants.Contains(Owner)) return;

        if (Owner.Player is not { } player) return;

        int leftover = Owner.Block;
        if (leftover <= 0) return;

        decimal summon = Math.Floor(leftover / 2m);
        if (summon <= 0) return;

        await OstyCmd.Summon(choiceContext, player, summon, this);
        Main.Log.Info($"化作尘泥：剩余 {leftover} 点格挡 → 转化为 {summon} 点召唤");
    }
}
