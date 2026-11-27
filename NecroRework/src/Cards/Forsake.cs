using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;

namespace NecroRework.Cards;

/// <summary>
/// 割舍：0 费技能，保留。
/// 奥斯提失去等同于自身缺失生命值的生命值，并失去全部灾厄；
/// 然后弃 2 张手牌，其中诅咒 / 状态牌先被附魔「飘荡」。
/// </summary>
public sealed class Forsake : CardModel
{
    /// <summary>弃几张牌。</summary>
    public const int DiscardCount = 2;

    private const ValueProp HpLossProps =
        ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    public Forsake()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        if (Owner.Osty is { IsAlive: true } osty)
        {
            int missing = (int)System.Math.Max(0m, osty.MaxHp - osty.CurrentHp);
            if (missing > 0)
            {
                await CreatureCmd.Damage(choiceContext, osty, missing, HpLossProps, this, cardPlay);
            }

            int doom = DoomUtil.DoomOf(osty);
            if (doom > 0)
            {
                await DoomUtil.RemoveDoom(choiceContext, this, osty, doom);
            }
        }

        CardPile hand = PileType.Hand.GetPile(Owner);
        int count = System.Math.Min(DiscardCount, hand.Cards.Count);
        if (count <= 0) return;

        var prefs = new CardSelectorPrefs(SelectionScreenPrompt, count);

        var picked = (await CardSelectCmd.FromHand(choiceContext, Owner, prefs, null, this)).ToList();
        if (picked.Count == 0) return;

        foreach (CardModel card in picked)
        {
            if (card.Enchantment != null) continue;
            if (card.Type != CardType.Curse && card.Type != CardType.Status) continue;

            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), card, 1m);
        }

        await CardCmd.Discard(choiceContext, picked);
    }

    // 卡面写「灾厄」「飘荡」，把这两个词条挂到卡旁边。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<DoomPower>(),
        .. HoverTipFactory.FromEnchantment<DriftEnchantment>()
    ];

    protected override void OnUpgrade()
    {
        // 升级：获得「保留」（敲前没有保留）。
        AddKeyword(CardKeyword.Retain);
    }
}
