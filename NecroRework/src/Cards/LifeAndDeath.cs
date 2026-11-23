using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 生与死：2（升级 1）费攻击（罕见/蓝卡），虚无。
/// 奥斯提造成等同于他缺失生命值的伤害；
/// 你接下来获得的 25 层灾厄改由奥斯提承担，但他在获得这些灾厄时也会受到等量伤害。
/// </summary>
public sealed class LifeAndDeath : CardModel
{
    private const decimal DoomToAbsorb = 25m;

    public LifeAndDeath()
        : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(0m),
        new ExtraDamageVar(1m),
        new CalculatedDamageVar(ValueProp.Move).FromOsty().WithMultiplier((CardModel card, Creature _) => MissingHpOf(card)),
    ];

    /// <summary>奥斯提缺失的生命值（不在场就按 0 算）。</summary>
    public static int MissingHpOf(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，访问 Owner 会抛 CanonicalModelException
        if (card.IsCanonical) return 0;
        if (card.Owner?.Osty is not { IsAlive: true } osty) return 0;

        return Math.Max(0, osty.MaxHp - osty.CurrentHp);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;
        if (Osty.CheckMissingWithAnim(Owner)) return;
        if (Owner.Osty is not { IsAlive: true } osty) return;

        int missing = MissingHpOf(this);

        if (missing > 0)
        {
            await DamageCmd.Attack(missing)
                .FromOsty(osty, this, cardPlay)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(choiceContext);
        }

        await PowerCmd.Apply<LifeAndDeathPower>(choiceContext, Owner.Creature, DoomToAbsorb, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
