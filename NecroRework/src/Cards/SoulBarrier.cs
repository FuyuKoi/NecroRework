using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 灵魂屏障：3（2）费技能，虚无。
/// 获得 20 点格挡；从抽牌堆选至多 3 张灵魂加入手牌；
/// 然后给手牌里所有灵魂附魔「飘荡」。
/// </summary>
public sealed class SoulBarrier : CardModel
{
    /// <summary>最多能抓几张灵魂。</summary>
    public const int MaxSouls = 3;

    private const decimal BaseBlock = 20m;

    public SoulBarrier()
        : base(3, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    // 卡面写「飘荡」「灵魂」，挂附魔词条与灵魂卡牌展示。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        .. HoverTipFactory.FromEnchantment<DriftEnchantment>(),
        HoverTipFactory.FromCard<Soul>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(BaseBlock, ValueProp.Move)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        var prefs = new CardSelectorPrefs(SelectionScreenPrompt, 0, MaxSouls);

        var souls = (await CardSelectCmd.FromCombatPile(
            choiceContext, PileType.Draw.GetPile(Owner), Owner, prefs,
            c => c is Soul)).ToList();

        foreach (CardModel soul in souls)
        {
            await CardPileCmd.Add(soul, PileType.Hand);
        }

        // 手牌里所有灵魂都附魔飘荡
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards.ToList())
        {
            if (card is not Soul) continue;
            if (card.Enchantment != null) continue;

            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), card, 1m);
        }
    }

    protected override void OnUpgrade()
    {
        // 升级只降费：3 → 2。
        EnergyCost.UpgradeBy(-1);
    }
}
