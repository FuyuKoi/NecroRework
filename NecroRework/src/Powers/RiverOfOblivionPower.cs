using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 相忘于川的持续效果：
/// 1) 你身上的灾厄会被原样压到奥斯提身上（你这边不留下灾厄）；
/// 2) 你受到的非攻击伤害改由奥斯提承受。
/// 攻击伤害不归这里管 —— 那是「为你而死」的 50% 分摊在做。
/// </summary>
public sealed class RiverOfOblivionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>非攻击伤害转给奥斯提。</summary>
    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props,
        Creature? dealer)
    {
        Player? player = Owner.Player;
        if (player == null) return target;
        if (target != player.Creature) return target;
        if (props.IsPoweredAttack()) return target;
        if (player.Osty is not { IsAlive: true } osty) return target;

        return osty;
    }

    /// <summary>你获得的灾厄 → 搬到奥斯提身上。</summary>
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is not DoomPower) return;
        if (amount <= 0m) return;

        Player? player = Owner.Player;
        if (player == null) return;
        if (power.Owner != player.Creature) return;
        if (player.Osty is not { IsAlive: true } osty) return;

        // 「生与死」在场且还有额度时，交给它处理这一笔：
        // 它会把额度内的层数搬走并让奥斯提失去等量生命值，
        // 剩下的层数（相忘于川的部分）它也会一起搬走、只是不掉血。
        // 这样两个能力的结算顺序固定成「生与死先、相忘于川后」，不会互相抢。
        if (Owner.GetPower<LifeAndDeathPower>() is { Amount: > 0 })
        {
            return;
        }

        try
        {
            // 先无判定地扣掉你身上这份，再压给奥斯提。
            // applier 传 null：转移这一步不再重复触发厄运之衣（你自己那次施加已经算过了）。
            int movable = (int)Math.Min(amount, power.Amount);
            if (movable <= 0) return;

            await PowerCmd.ModifyAmount(choiceContext, power, -movable, null, cardSource);
            await PowerCmd.Apply<DoomPower>(choiceContext, osty, movable, null, cardSource);
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 相忘于川转移灾厄失败: {e.Message}");
        }
    }
}
