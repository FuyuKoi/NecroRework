using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using NecroRework.Enchantments;
using NecroRework.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 聚魂术：1（0）费能力，虚无。
/// 打出时给抽牌堆里 2 张牌附魔「飘荡」；
/// 之后每个回合开始时，获得一张附魔「飘荡」的升级版灵魂。
/// </summary>
public sealed class SoulGather : CardModel
{
    /// <summary>打出时给抽牌堆几张牌附魔。</summary>
    public const int EnchantCount = 2;

    public SoulGather()
        : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    // 卡面写「飘荡」「灵魂+」，挂附魔词条与升级版灵魂的展示。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        .. HoverTipFactory.FromEnchantment<DriftEnchantment>(),
        HoverTipFactory.FromCard<Soul>(true)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);

        var prefs = new CardSelectorPrefs(
            new LocString("cards", "SOUL_GATHER.selectionScreenPrompt"), EnchantCount);

        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext, PileType.Draw.GetPile(Owner), Owner, prefs,
            c => c.Enchantment == null)).ToList();

        foreach (CardModel card in picked)
        {
            CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), card, 1m);
            CardCmd.Preview(card);
        }

        await PowerCmd.Apply<SoulGatherPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        // 升级只降费：1 → 0。
        EnergyCost.UpgradeBy(-1);
    }
}
