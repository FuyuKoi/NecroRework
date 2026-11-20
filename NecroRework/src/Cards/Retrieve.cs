using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Enchantments;

namespace NecroRework.Cards;

/// <summary>
/// 找回：0 费技能（普通品质），消耗。
/// 获得 3（升级 5）点格挡、召唤 1（升级 2），
/// 然后把自己的一张复制品附魔「飘荡」放进弃牌堆。
/// </summary>
public sealed class Retrieve : CardModel
{
    public Retrieve()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(3m, ValueProp.Move), new SummonVar(1m)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        await CreatureCmd.TriggerAnim(Owner.Creature,
            Necrobinder.GetSummonAnimIfApplicable(Owner.Character),
            Necrobinder.GetSummonDelayIfApplicable(Owner.Character));

        await OstyCmd.Summon(choiceContext, Owner, DynamicVars.Summon.BaseValue, this);

        // 复制品：保留本牌的升级状态，附魔飘荡后进入弃牌堆。
        // 注意：本牌自己如果已经被附魔（比如被「聚拢」加了飘荡），复制品会连着附魔一起复制过来，
        // 这时再 Enchant 会因为「已经有附魔」直接抛异常、把整次出牌卡死。
        // 所以先清掉复制品身上的附魔，再统一附魔飘荡。
        var copy = CreateClone();
        if (copy.Enchantment != null)
        {
            CardCmd.ClearEnchantment(copy);
        }

        CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), copy, 1m);

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(
            copy, PileType.Discard, Owner));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2m);
        DynamicVars.Summon.UpgradeValueBy(1m);
    }
}
