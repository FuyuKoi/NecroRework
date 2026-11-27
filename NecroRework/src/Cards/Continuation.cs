using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 延续：0 费技能（普通品质）。
/// 获得 1 点费用；挂上「延续」能力——回合结束时没花完的费用像「冰淇淋」一样带到下一回合，
/// 并按带过去的费用 × 2 倍层数给玩家上灾厄。升级后获得「保留」。
/// </summary>
public sealed class Continuation : CardModel
{
    public Continuation()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new IntVar("DoomMult", 2m)];

    // 卡面写「能量」，挂能量词条。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.ForEnergy(this)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        await PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue, Owner);

        // 倍数直接当作能力的层数存着：升级前后都是 2（重复打出会相加）。
        await PowerCmd.Apply<EnergyCarryPower>(
            choiceContext, Owner.Creature, DynamicVars["DoomMult"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        // 升级只加「保留」，倍数不变
        AddKeyword(CardKeyword.Retain);
    }
}
