using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 生与死：接下来你获得的 N 层灾厄由奥斯提承担（算作你施加给他），
/// 而他在承受这些灾厄时，会受到等量伤害。
/// 挡住多少层就消耗多少层，用完自动消失。
///
/// 结算口径（与「相忘于川」统一）：
///   1) 灾厄先**正常落到你自己身上** —— 厄运之衣 / 死亡之门这些
///      「本回合你施加过灾厄」的判定按这一笔算一次；
///   2) 再把等量层数搬到奥斯提身上，applier 传 null，
///      转移本身不再重复触发厄运之衣（否则一笔灾厄会算两次格挡）；
///   3) 只有「生与死额度内」的那部分才会让奥斯提失去等量生命值。
/// </summary>
public sealed class LifeAndDeathPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// 灾厄先落在自己身上（引擎照常触发 ShroudPower / 死亡之门），
    /// 这里再把层数搬到奥斯提身上。
    /// 用「当前层数」夹住 amount：同一次施加流程里钩子可能被调用多次。
    /// </summary>
    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power is not DoomPower) return;
        if (amount <= 0m) return;
        if (power.Owner != Owner) return;
        if (Owner.Player is not { Osty: { IsAlive: true } osty }) return;

        int movable = (int)Math.Min(amount, power.Amount);
        if (movable <= 0) return;

        int budget = Math.Max(0, Amount);
        int absorb = Math.Min(budget, movable);
        bool river = Owner.HasPower<RiverOfOblivionPower>();
        int rest = river ? movable - absorb : 0;

        if (absorb <= 0 && rest <= 0) return;

        var choice = new ThrowingPlayerChoiceContext();
        int before = (int)DoomUtil.DoomOf(osty);

        // 先无判定地从自己身上扣掉要转移的层数（applier = null → 不产生任何「施加」判定）
        try
        {
            await PowerCmd.ModifyAmount(choiceContext, power, -(absorb + rest), null, cardSource);
            // 再原样压给奥斯提；applier 传 null，转移这一步不再触发厄运之衣
            await PowerCmd.Apply<DoomPower>(choice, osty, absorb + rest, null, cardSource);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 生与死：转移灾厄失败: {e.Message}");
            return;
        }

        int after = (int)DoomUtil.DoomOf(osty);
        int gained = Math.Max(0, after - before);
        Main.Log.Info($"[{Main.ModId}] 生与死：搬到奥斯提 {absorb + rest} 层（额度内 {absorb}），奥斯提灾厄 {before}→{after}");

        if (absorb > 0)
        {
            // 「失去等量生命值」而不是「受到伤害」：
            // Unblockable（不吃格挡、不是被打）、Unpowered（力量/易伤/荆棘/反射都不参与），
            // 但依旧走完整伤害管线，所以数字、死亡判定、厄运之衣这些都正常。
            await CreatureCmd.Damage(choice, osty, absorb, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
            Main.Log.Info($"[{Main.ModId}] 生与死：奥斯提失去 {absorb} 点生命值，剩余生命 {osty.CurrentHp}");

            await PowerCmd.ModifyAmount(choiceContext, this, -absorb, Owner, null);
        }

        _ = gained;
    }
}
