using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 透支：2 费，虚无。获得 4（5）费，并在本回合获得 2 层「死亡律动」。
/// 死亡律动固定 2 层、不可再叠加（见 DeathRhythmPower）。
/// </summary>
public sealed class Overdraft : CardModel
{
    private const int RhythmStacks = 2;

    public Overdraft()
        : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(4)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        int energy = DynamicVars.Energy.IntValue;
        if (energy > 0)
            await PlayerCmd.GainEnergy(energy, Owner);

        // 已经有了就不再加：能力自己也会拒绝第二次施加（不可叠加）
        if (Owner.Creature.GetPower<DeathRhythmPower>() == null)
            await PowerCmd.Apply<DeathRhythmPower>(choiceContext, Owner.Creature, RhythmStacks, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Energy.UpgradeValueBy(1m);
    }
}
