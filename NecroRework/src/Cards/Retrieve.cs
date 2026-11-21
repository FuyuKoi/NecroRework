using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 找回：0 费技能（普通品质），虚无 + 消耗。
/// 获得 3（升级 5）点格挡，返还等同于本牌实际费用的能量（本体 0 费返 0，复制品 1 费返 1……），
/// 然后把自己的一张复制品放进弃牌堆：复制品的费用 +1、格挡 +3（升级后 +5），于是每一代都比上一代更贵更强。
/// </summary>
public sealed class Retrieve : CardModel
{
    /// <summary>每一代复制品增加的费用。</summary>
    private const int CostIncrease = 1;

    /// <summary>每一代复制品增加的格挡（未升级 / 升级后）。</summary>
    private const decimal BlockIncrease = 3m;

    private const decimal BlockIncreaseUpgraded = 5m;

    public Retrieve()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(3m, ValueProp.Move)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust, CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 返还等同于本牌当前费用的能量（= 打出这张牌实际付掉的费用）。
        // 取的是「带修正后的实际费用」，所以临时的加减费也会算进去。
        int cost = EnergyCost.GetWithModifiers(CostModifiers.All);
        if (cost > 0)
        {
            await PlayerCmd.GainEnergy(cost, Owner);
        }

        // 复制品：保留本牌的升级状态，费用 +1、格挡 +3（升级后 +5），然后进入弃牌堆。
        // 本牌自己如果被别人附魔过，复制品会连着附魔一起复制过来，所以这里先清掉附魔
        // （清掉之后再附魔也不会抛异常，等于复制品是干净的）。
        var copy = CreateClone();
        if (copy.Enchantment != null)
        {
            CardCmd.ClearEnchantment(copy);
        }

        copy.EnergyCost.UpgradeBy(CostIncrease);
        copy.EnergyCost.FinalizeUpgrade();
        copy.DynamicVars.Block.BaseValue += IsUpgraded ? BlockIncreaseUpgraded : BlockIncrease;

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(
            copy, PileType.Discard, Owner));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2m);
    }
}
