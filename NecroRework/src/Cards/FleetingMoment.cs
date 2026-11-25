using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 飞逝：0 费技能（蓝色/罕见，升级后每张消耗多抽 1 张）。保留。
/// 打出后获得「飞逝」能力：本回合内每消耗 1 张牌，自动抽 1（升级 2）张，并获得 1 层精神过载。
/// 回合结束时因虚无自动消耗的牌，抽牌会等到手牌弃完之后才结算，抽到的牌留到下个回合。
/// </summary>
public sealed class FleetingMoment : CardModel
{
    public FleetingMoment()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    /// <summary>Cards = 每消耗 1 张牌抽几张。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    /// <summary>常驻保留。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<NeurosurgePower>()];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        // 能力层数 = 每消耗 1 张牌抽几张（1 或升级后的 2）。
        await PowerCmd.Apply<FleetingMomentPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Cards.IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1m);
    }
}
