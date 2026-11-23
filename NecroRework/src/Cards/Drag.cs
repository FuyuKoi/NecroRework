using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 拖拽：2 费攻击（罕见/蓝卡），虚无。
/// 造成 21 点伤害；如果自己身上至少有 13（升级 19）层灾厄，额外造成 15（升级 20）点伤害。
/// 阈值按打出瞬间的灾厄层数判断，两种情况都只打一发。
/// </summary>
public sealed class Drag : CardModel
{
    private const decimal BaseDamage = 21m;

    private const decimal ExtraDamage = 15m;

    private const decimal ExtraDamageUpgraded = 20m;

    private const int DoomThreshold = 19;

    private const int DoomThresholdUpgraded = 13;

    public Drag()
        : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(BaseDamage, ValueProp.Move)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;

        decimal bonus = DoomThresholdReached(this)
            ? (IsUpgraded ? ExtraDamageUpgraded : ExtraDamage)
            : 0m;

        await DamageCmd.Attack(BaseDamage + bonus)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
    }

    private static bool DoomThresholdReached(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，访问 Owner 会抛 CanonicalModelException
        if (card.IsCanonical) return false;
        if (card.Owner is null) return false;

        int threshold = card.IsUpgraded ? DoomThresholdUpgraded : DoomThreshold;
        return DoomUtil.PlayerDoom(card) >= threshold;
    }

    /// <summary>
    /// 卡面：能吃到额外伤害时，在基础伤害后面补一个绿色括号显示本次实际伤害，例如「21（36）」。
    /// 吃不到时不显示括号。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        if (DoomThresholdReached(this))
        {
            decimal total = BaseDamage + (IsUpgraded ? ExtraDamageUpgraded : ExtraDamage);
            description.Add("DamageBonus", $"[green]（{total:0}）[/green]");
            return;
        }

        description.Add("DamageBonus", string.Empty);
    }

    protected override void OnUpgrade()
    {
        // 基础伤害固定 21，升级只抬高「额外伤害」与灾厄阈值，数值在 OnPlay 里按 IsUpgraded 取。
    }
}
