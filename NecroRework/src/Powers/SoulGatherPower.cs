using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using NecroRework.Enchantments;

namespace NecroRework.Powers;

/// <summary>
/// 聚魂术的持续效果：每个回合开始时，生成一张「灵魂+」并附魔「飘荡」放进手牌。
/// </summary>
public sealed class SoulGatherPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (!participants.Contains(Owner)) return;

        Player? player = Owner.Player;
        if (player?.PlayerCombatState == null) return;

        try
        {
            Soul soul = combatState.CreateCard<Soul>(player);
            soul.UpgradeInternal();
            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), soul, 1m);

            Flash();
            await CardPileCmd.AddGeneratedCardToCombat(soul, PileType.Hand, player);
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 聚魂术生成灵魂失败: {e.Message}");
        }
    }
}
