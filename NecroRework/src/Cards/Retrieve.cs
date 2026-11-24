using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 找回：0 费技能（普通品质），虚无 + 消耗。
/// 获得 4（升级 6）点格挡与 4（升级 6）点召唤，返还打出本牌实际消耗的能量，
/// 然后把自己的一张复制品放进弃牌堆：复制品的费用 +1、格挡与召唤各 +4，
/// 于是每一代都比上一代更贵更强。
/// </summary>
public sealed class Retrieve : CardModel
{
    /// <summary>每一代复制品增加的费用。</summary>
    private const int CostIncrease = 1;

    /// <summary>每一代复制品增加的格挡。</summary>
    private const decimal BlockIncrease = 4m;

    /// <summary>每一代复制品增加的召唤。</summary>
    private const decimal SummonIncrease = 4m;

    public Retrieve()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(4m, ValueProp.Move), new SummonVar(4m), new EnergyVar(1)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust, CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await OstyCmd.Summon(choiceContext, Owner, DynamicVars.Summon.BaseValue, this);

        // 返还「打出这张牌实际消耗掉的能量」。
        // 用 CardPlay 里记的实付费用，而不是费用面板的当前值：
        // 这样被刺破帷幕减成 0 费、被预借时间加成 2 费时，返还永远等于真正掏出去的那份。
        int spent = cardPlay.Resources.EnergySpent;
        if (spent > 0)
        {
            await PlayerCmd.GainEnergy(spent, Owner);
        }

        // 复制品：保留本牌的升级状态，费用 +1、格挡 +4（升级后 +6）、召唤 +3，然后进入弃牌堆。
        // 本牌自己如果被别人附魔过，复制品会连着附魔一起复制过来，所以这里先清掉附魔。
        var copy = CreateClone();
        if (copy.Enchantment != null)
        {
            CardCmd.ClearEnchantment(copy);
        }

        copy.EnergyCost.UpgradeBy(CostIncrease);
        copy.EnergyCost.FinalizeUpgrade();
        copy.DynamicVars.Block.BaseValue += BlockIncrease;
        copy.DynamicVars.Summon.BaseValue += SummonIncrease;

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(
            copy, PileType.Discard, Owner));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(5m);
        DynamicVars.Summon.UpgradeValueBy(5m);
    }
}
