using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Enchantments;

namespace NecroRework.Cards;

/// <summary>
/// 聚拢：0 费技能（普通品质），虚无。
/// 抽 1 张牌，然后给手牌中的一张牌附魔「飘荡」。
/// </summary>
public sealed class Gather : CardModel
{
    public Gather()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        await CardPileCmd.Draw(choiceContext, 1m, Owner);

        var prefs = new CardSelectorPrefs(
            new LocString("cards", "GATHER.selectionScreenPrompt"), 1);

        var picked = await CardSelectCmd.FromHand(
            choiceContext, Owner, prefs,
            c => c.Enchantment == null,
            this);

        var target = picked.FirstOrDefault();
        if (target == null) return;

        CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), target, 1m);
        CardCmd.Preview(target);
    }

    protected override void OnUpgrade()
    {
        // 升级后变成固有：第一回合必定上手。
        AddKeyword(CardKeyword.Innate);
    }
}
