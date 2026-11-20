using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 回眸：1 费技能（普通品质）。
/// 获得 7（升级 10）点格挡，然后把「消耗牌堆」里的一张灵魂、
/// 或者一张没有「消耗」关键词的虚无牌，放到抽牌堆顶。
/// </summary>
public sealed class LookBack : CardModel
{
    public LookBack()
        : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(7m, ValueProp.Move)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        var pile = PileType.Exhaust.GetPile(Owner);
        var picked = await CardSelectCmd.FromCombatPile(
            choiceContext,
            pile,
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 1),
            IsRecallable);

        var card = picked.FirstOrDefault();
        if (card == null) return;

        await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Top);
    }

    /// <summary>灵魂本身带「消耗」，所以先单独放行；其余要求「虚无」且不带「消耗」。</summary>
    private static bool IsRecallable(CardModel card)
    {
        if (card is Soul) return true;

        return card.Keywords.Contains(CardKeyword.Ethereal)
               && !card.Keywords.Contains(CardKeyword.Exhaust);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
    }
}
