using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 升华：1 费攻击（罕见/蓝卡），虚无。
/// 造成 16 点伤害；这张牌的耗能每变化 1 点，本场战斗中的伤害就 +8（升级 +12）。
/// 打出过一次之后，每次再抽到这张牌，它的耗能永久 +1。
/// </summary>
public sealed class Sublimation : CardModel
{
    public const decimal BaseDamage = 16m;

    public const decimal DamagePerCostStep = 8m;

    public const decimal DamagePerCostStepUpgraded = 12m;

    private static readonly ConditionalWeakTable<CardModel, StrongBox<int>> StepTable = new();

    private static readonly HashSet<CardModel> PlayedCards = new(ReferenceEqualityComparer.Instance);

    public Sublimation()
        : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(DamagePerCostStep),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier((CardModel card, Creature _) => CostStepsOf(card)),
    ];

    /// <summary>这张牌累计「耗能变化」了多少点。</summary>
    public static int CostStepsOf(CardModel card)
    {
        return StepTable.TryGetValue(card, out StrongBox<int> box) ? box.Value : 0;
    }

    /// <summary>记录一次耗能变化（变化了几点就记几点）。</summary>
    public static void AddCostSteps(CardModel card, int steps)
    {
        if (steps <= 0) return;

        StrongBox<int> box = StepTable.GetValue(card, static _ => new StrongBox<int>(0));
        box.Value += steps;
    }

    public static bool HasBeenPlayed(CardModel card) => PlayedCards.Contains(card);

    public static decimal DamageOf(CardModel card)
    {
        decimal perStep = card.IsUpgraded ? DamagePerCostStepUpgraded : DamagePerCostStep;
        return BaseDamage + (CostStepsOf(card) * perStep);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;

        PlayedCards.Add(this);

        await DamageCmd.Attack(DamageOf(this))
            .FromCard(this, cardPlay)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.ExtraDamage.UpgradeValueBy(DamagePerCostStepUpgraded - DamagePerCostStep);
    }
}
