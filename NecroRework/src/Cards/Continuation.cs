using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 延续：0 费技能（普通品质）。
/// 召唤 4；挂上「延续」能力——回合结束时没花完的费用像「冰淇淋」一样带到下一回合，
/// 并按带过去的费用 × 3 倍（升级 2 倍）层数给玩家上灾厄。
/// </summary>
public sealed class Continuation : CardModel
{
    public Continuation()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new SummonVar(4m), new IntVar("DoomMult", 3m)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature,
            Necrobinder.GetSummonAnimIfApplicable(Owner.Character),
            Necrobinder.GetSummonDelayIfApplicable(Owner.Character));

        await OstyCmd.Summon(choiceContext, Owner, DynamicVars.Summon.BaseValue, this);

        // 倍数直接当作能力的层数存着：未升级 3、升级后 2（重复打出会相加）。
        await PowerCmd.Apply<EnergyCarryPower>(
            choiceContext, Owner.Creature, DynamicVars["DoomMult"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        // 三倍 → 两倍
        DynamicVars["DoomMult"].UpgradeValueBy(-1m);
    }
}
