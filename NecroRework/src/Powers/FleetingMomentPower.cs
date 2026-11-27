using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 飞逝：本回合内每消耗 1 张牌，就自动抽 Amount 张牌，并获得 Amount 层精神过载（类似「黑暗之拥」）。
/// Amount 是多次打出的累加值（未升级 1 + 升级 2 = 3），图标上显示的就是这个总层数。
///
/// 回合结束的特殊时序：虚无牌是在 DoTurnEnd 里先自动消耗的，而手牌要到
/// EndPlayerTurnPhaseTwo 的 FlushPlayerHand 才丢弃，最后才是 Hook.AfterSideTurnEnd。
/// 所以「结束阶段」里消耗造成的抽牌不立刻抽（否则刚抽到的牌会被马上弃掉），
/// 而是先攒着，等 AfterSideTurnEnd（手牌已经弃完）再一次性抽 —— 抽到的牌就能留到下个回合。
/// </summary>
public sealed class FleetingMomentPower : PowerModel
{
    private int _pendingDraw;
    private int _pendingDoom;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>悬停这个能力时，把「精神过载」的说明一起挂出来（数值 = 每消耗 1 张给的层数）。</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new IHoverTip[1] { HoverTipFactory.FromPower<NeurosurgePower>(Math.Max(1, Amount)) };

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        try
        {
            if (Owner == null || !Owner.IsAlive) return;
            if (Owner.Player is not { } player) return;
            if (card.Owner != player) return;

            int draw = Math.Max(1, Amount);
            // 精神过载（下回合开始转成灾厄）固定 1 层：升级只加抽牌数，不加这个层数。
            const int doomPerTrigger = 1;

            if (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo)
            {
                _pendingDraw += draw;
                _pendingDoom += doomPerTrigger;
                Main.Log.Info($"[{Main.ModId}] 飞逝：结束阶段消耗 {card.Id} → 攒下 {draw} 张抽牌（手牌弃完后再抽）");
                return;
            }

            Flash();
            await CardPileCmd.Draw(choiceContext, draw, player);
            await PowerCmd.Apply<NeurosurgePower>(choiceContext, Owner, doomPerTrigger, Owner, null);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飞逝：消耗触发抽牌失败: {e}");
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (participants == null || !participants.Contains(Owner)) return;

        try
        {
            if ((_pendingDraw > 0 || _pendingDoom > 0) && Owner.Player is { } player)
            {
                Main.Log.Info($"[{Main.ModId}] 飞逝：弃牌结束后补抽 {_pendingDraw} 张、精神过载 +{_pendingDoom} 层");

                if (_pendingDraw > 0)
                    await CardPileCmd.Draw(choiceContext, _pendingDraw, player);

                if (_pendingDoom > 0)
                    await PowerCmd.Apply<NeurosurgePower>(choiceContext, Owner, _pendingDoom, Owner, null);
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飞逝：回合结束补抽失败: {e}");
        }
        finally
        {
            _pendingDraw = 0;
            _pendingDoom = 0;
        }

        await PowerCmd.Remove(this);
    }
}
