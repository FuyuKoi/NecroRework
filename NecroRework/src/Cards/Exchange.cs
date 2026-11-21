using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 交换：0 费技能（普通品质），固有 + 保留 + 消耗。
/// 选择手牌中至多 2 张虚无牌，让它们下次打出的费用 -1；
/// 每选中一张，你获得 6（升级 4）层灾厄；最后弃一张牌。
/// </summary>
public sealed class Exchange : CardModel
{
    public Exchange()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<DoomPower>(6m)];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Innate, CardKeyword.Retain, CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        // 至多 2 张：MinSelect = 0 → 引擎会显示确认按钮，可以只选 0/1 张。
        var prefs = new CardSelectorPrefs(
            new LocString("cards", "EXCHANGE.selectionScreenPrompt"), 0, 2);

        var picked = await CardSelectCmd.FromHand(
            choiceContext, Owner, prefs,
            c => c.Keywords.Contains(CardKeyword.Ethereal),
            this);

        var count = 0;
        foreach (var card in picked)
        {
            card.EnergyCost.AddUntilPlayed(-1, reduceOnly: true);
            card.InvokeEnergyCostChanged();
            count++;
        }

        if (count > 0)
            await DoomUtil.ApplyToSelf(choiceContext, this, DynamicVars.Doom.BaseValue * count);

        // 弃一张牌（手牌为空时直接跳过）。
        if (PileType.Hand.GetPile(Owner).Cards.Count == 0) return;

        var discardPrefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1);
        var toDiscard = await CardSelectCmd.FromHand(choiceContext, Owner, discardPrefs, null, this);

        foreach (var card in toDiscard)
            await CardCmd.Discard(choiceContext, card);
    }

    protected override void OnUpgrade()
    {
        // 灾厄 6 → 4
        DynamicVars.Doom.UpgradeValueBy(-2m);
    }
}
