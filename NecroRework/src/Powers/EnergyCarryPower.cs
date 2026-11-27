using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NecroRework.Powers;

/// <summary>
/// 延续（挂在玩家身上，1 回合）：
/// 回合结束时剩余的费用不会重置，而是像「冰淇淋」一样原样带进下一回合；
/// 同时在回合结束时按剩余费用 × Amount 倍给玩家自己上灾厄。
///
/// Amount = 倍数（未升级 3、升级 2），所以重复打出会相加。
/// 灾厄以「玩家自己作为施加者」的方式施加，会正常触发死亡之门 / 厄运之衣，不触发血肉戏法。
/// </summary>
public sealed class EnergyCarryPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// 只为说明文字里能画出能量图标（EnergyIconsFormatter 只吃 EnergyVar 这类值，
    /// 直接写 {Amount} 会是纯数字）。数值固定 1，实际保留多少费用由引擎决定。
    /// </summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    /// <summary>本能力在场时，玩家的费用不做重置（引擎会改成「当前费用 + 本回合最大费用」）。</summary>
    public override bool ShouldPlayerResetEnergy(Player player)
    {
        if (Owner is not { IsAlive: true } me) return true;
        if (player != me.Player) return true;

        return Amount <= 0;
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (Owner is not { IsAlive: true } me) return;

        // 此刻还没重置能量，所以这里读到就是「按下结束时剩下的费用」。
        var energy = me.Player?.PlayerCombatState?.Energy ?? 0;
        if (energy <= 0) return;

        await PowerCmd.Apply<DoomPower>(choiceContext, me, energy * Amount, me, null);
    }

    /// <summary>费用已经带过去了，本能力的使命到此为止。</summary>
    public override async Task AfterEnergyReset(Player player)
    {
        if (Owner is not { IsAlive: true } me) return;
        if (player != me.Player) return;

        await PowerCmd.Remove(this);
    }
}
