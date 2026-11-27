using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 初心：每回合开始时，把上回合最先被动消耗的 3 张虚无牌放回手牌，并获得 6 点格挡。
/// </summary>
public sealed class BeginnerMindPower : PowerModel
{
    private const decimal BlockPerTurn = 5m;
    private const int RecallCount = 3;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (Owner is not { IsAlive: true } me) return;

        Player? player = me.Player;
        if (player == null) return;

        List<CardModel> recall = PassiveExhaustLog.TakeLastTurn(player, RecallCount);

        if (recall.Count > 0)
        {
            Main.Log.Info($"[{Main.ModId}] 初心：回收上回合被动消耗的 {recall.Count} 张牌（{string.Join("/", recall.Select(c => c.Id.ToString()))}）");
            Flash();
            foreach (CardModel card in recall)
            {
                await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Bottom);
            }
        }
        else
        {
            Main.Log.Info($"[{Main.ModId}] 初心：上回合没有可回收的被动消耗虚无牌（{PassiveExhaustLog.Dump(player)}）");
        }

        await CreatureCmd.GainBlock(me, BlockPerTurn, ValueProp.Move, null);
    }
}
