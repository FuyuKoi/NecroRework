using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;

namespace NecroRework.Powers;

/// <summary>
/// 灰烬之灵（重做）：每打出一张「虚无」牌获得 5 × 层数 点格挡；
/// 回合开始时可为一至「层数」张手牌附魔「飘荡」。
/// 层数按「身上所有同名能力的层数之和」算：
/// 多次打出这张能力牌时，引擎有时会叠加到同一个实例上，有时会另开一个实例，
/// 只看单个实例的 Amount 会出现「附魔数涨了、格挡没涨」的情况。
/// </summary>
public sealed class SpiritOfAshReworkPower : PowerModel
{
    /// <summary>每张虚无牌给的格挡（卡面文字也从这里取，改一处两边同步）。</summary>
    public const decimal BlockPerLayer = 3m;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// 卡面/悬停里的数字必须跟着层数走：{Block} 是「当前每张虚无牌给的格挡」，
    /// {Amount} 是「回合开始能附魔几张」。图鉴(canonical)里没有 Owner，按 1 层显示。
    /// </summary>
    public override LocString Description
    {
        get
        {
            int layers = Owner == null ? 1 : Math.Max(1, TotalLayers);
            LocString loc = new("powers", Id.Entry + ".description");
            loc.Add("Block", BlockPerLayer * layers);
            loc.Add("Amount", layers);
            return loc;
        }
    }

    /// <summary>身上所有「灰烬之灵」实例的层数之和。</summary>
    private int TotalLayers
    {
        get
        {
            if (Owner == null) return 0;

            int total = 0;
            foreach (PowerModel power in Owner.Powers)
            {
                if (power is SpiritOfAshReworkPower ash)
                {
                    total += ash.Amount;
                }
            }

            return total;
        }
    }

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (Owner is not { IsAlive: true } owner) return;
        if (cardPlay.Card.Owner?.Creature != owner) return;
        if (!cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal)) return;

        int layers = TotalLayers;
        if (layers <= 0) return;

        decimal block = BlockPerLayer * layers;
        Main.Log.Info($"[{Main.ModId}] 灰烬之灵：{cardPlay.Card.Id} 虚无，层数 {layers}，获得 {block} 点格挡");
        await CreatureCmd.GainBlock(owner, block, ValueProp.Unpowered, null);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner is not { IsAlive: true } owner) return;
        if (player.Creature != owner) return;

        int picks = TotalLayers;
        if (picks <= 0) return;

        CardPile hand = PileType.Hand.GetPile(player);
        if (hand.Cards.Count == 0) return;

        LocString prompt = SelectionScreenPrompt;
        prompt.Add("Picks", picks);

        var chosen = (await CardSelectCmd.FromHand(choiceContext, player,
            new CardSelectorPrefs(prompt, 0, picks),
            card => card.Enchantment == null, this)).ToList();

        Main.Log.Info($"[{Main.ModId}] 灰烬之灵：回合开始可附魔 {picks} 张，实际附魔 {chosen.Count} 张");

        foreach (CardModel card in chosen)
        {
            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), card, 1m);
            CardCmd.Preview(card);
        }
    }

    private static LocString SelectionScreenPrompt => new("cards", "SPIRIT_OF_ASH.selectionScreenPrompt");
}
