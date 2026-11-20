using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Powers;

/// <summary>
/// 冥灯（挂在玩家身上）：
/// Amount = 还要生效的回合数。生效期间每回合抽到的前两张[虚无]牌，各给予玩家 1 点费用。
/// 打出当回合不生效（打出的那个回合结束时只「上膛」，不递减）。
/// </summary>
public sealed class GhostLanternPower : PowerModel
{
    /// <summary>每回合最多触发的次数。</summary>
    private const int TriggersPerTurn = 2;

    /// <summary>每次触发给的能量。</summary>
    private const int EnergyPerTrigger = 1;

    private sealed class Data
    {
        /// <summary>本回合已经触发过几次。</summary>
        public int UsedThisTurn;

        /// <summary>是否已经进入生效期（打出当回合为 false）。</summary>
        public bool Armed;
    }

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>悬停里显示的读数 = 剩余回合数。</summary>
    public override int DisplayAmount => Amount;

    protected override object InitInternalData() => new Data();

    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (Owner is not { IsAlive: true } me) return;

        var data = GetInternalData<Data>();
        if (!data.Armed) return;
        if (card.Owner != me.Player) return;
        if (!card.Keywords.Contains(CardKeyword.Ethereal)) return;
        if (data.UsedThisTurn >= TriggersPerTurn) return;

        data.UsedThisTurn++;
        Flash();
        await PlayerCmd.GainEnergy(EnergyPerTrigger, me.Player);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (Owner is not { IsAlive: true }) return;

        var data = GetInternalData<Data>();

        // 打出当回合：只上膛，不消耗持续回合数。
        if (!data.Armed)
        {
            data.Armed = true;
            data.UsedThisTurn = 0;
            return;
        }

        data.UsedThisTurn = 0;
        await PowerCmd.Decrement(this);
    }
}
