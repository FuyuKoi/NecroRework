using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;

namespace NecroRework.Powers;

/// <summary>
/// 灰烬之灵（重做）：每打出一张「虚无」牌获得 5 × 层数 点格挡；
/// 回合开始时可为一至「层数」张手牌附魔「飘荡」。
/// </summary>
public sealed class SpiritOfAshReworkPower : PowerModel
{
    private const decimal BlockPerLayer = 5m;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (Owner is not { IsAlive: true } owner) return;
        if (cardPlay.Card.Owner?.Creature != owner) return;
        if (!cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal)) return;

        await CreatureCmd.GainBlock(owner, BlockPerLayer * Amount, ValueProp.Unpowered, null);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner is not { IsAlive: true } owner) return;
        if (player.Creature != owner) return;

        int picks = Amount;
        if (picks <= 0) return;

        CardPile hand = PileType.Hand.GetPile(player);
        if (hand.Cards.Count == 0) return;

        var chosen = (await CardSelectCmd.FromHand(choiceContext, player,
            new CardSelectorPrefs(SelectionScreenPrompt, 0, picks),
            card => card.Enchantment == null, this)).ToList();

        foreach (CardModel card in chosen)
        {
            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), card, 1m);
            CardCmd.Preview(card);
        }
    }

    private static MegaCrit.Sts2.Core.Localization.LocString SelectionScreenPrompt =>
        new("cards", "SPIRIT_OF_ASH.selectionScreenPrompt");
}
