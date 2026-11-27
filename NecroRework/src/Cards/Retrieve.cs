using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 找回：0 费技能（普通品质），虚无 + 消耗。
/// 获得 6（升级 8）点格挡与 7（升级 9）点召唤；
/// 打出后，这张牌在本场战斗中的费用 +1（0 → 1 → 2 …），越打越贵。
/// </summary>
public sealed class Retrieve : CardModel
{
    /// <summary>每打出一次，这张牌在本场战斗里的费用就涨这么多。</summary>
    private const int CostIncreasePerPlay = 1;

    public Retrieve()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(6m, ValueProp.Move), new SummonVar(7m), new EnergyVar(CostIncreasePerPlay)];

    // 按用户要求：找回不再带「消耗」（只保留「虚无」，回合结束留在手牌里仍会烧掉）。
    // 注意：AfterCardExhausted 那条「被消耗时也触发」的逻辑仍然有效（虚无烧掉同样算消耗）。
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    // 卡面写「召唤」「能量」，两个词条都挂出来。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.SummonDynamic, DynamicVars.Summon),
        HoverTipFactory.ForEnergy(this)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await OstyCmd.Summon(choiceContext, Owner, DynamicVars.Summon.BaseValue, this);

        // 打出后自身本场战斗费用 +1：下一次再见到它（保留/回手/洗回抽牌堆）就更贵。
        EnergyCost.AddThisCombat(CostIncreasePerPlay);
        InvokeEnergyCostChanged();
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2m);
        DynamicVars.Summon.UpgradeValueBy(2m);
    }
}
