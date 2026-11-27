using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 凝望：1 费技能（罕见）。下回合获得 2 点费用，并额外抽 1（2）张牌。
/// 这张牌被消耗时同样触发完整效果。
/// </summary>
public sealed class Gaze : CardModel
{
    public Gaze()
        : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar(1),
        new CardsVar(1),
    ];

    // 卡面写「消耗」，这张牌本身没有消耗关键词（是被消耗时触发），所以手动挂词条；
    // 再加上「能量」的说明（牌面写着下回合获得的费用）。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.ForEnergy(this)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (IsCanonical) return;

        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await ApplyDelayedGain(choiceContext);
    }

    /// <summary>被消耗（虚无自燃、被别的牌烧掉等）时同样触发完整效果。</summary>
    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if (card != this) return;
        if (IsCanonical || Owner is null) return;

        await ApplyDelayedGain(choiceContext);
    }

    private async Task ApplyDelayedGain(PlayerChoiceContext choiceContext)
    {
        await PowerCmd.Apply<EnergyNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Energy.IntValue,
            Owner.Creature,
            this);

        await PowerCmd.Apply<DrawCardsNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Cards.IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Energy.UpgradeValueBy(1m);
        DynamicVars.Cards.UpgradeValueBy(1m);
    }
}
