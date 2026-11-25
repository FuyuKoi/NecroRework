using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 生与死：2（升级 1）费攻击（罕见/蓝卡），虚无。
/// 奥斯提造成等同于「他缺失的生命值 + 他身上的灾厄层数」的伤害；
/// 你接下来获得的 25 层灾厄改由奥斯提承担，但他在获得这些灾厄时也会受到等量伤害。
/// </summary>
public sealed class LifeAndDeath : CardModel
{
    private const decimal DoomToAbsorb = 20m;

    public LifeAndDeath()
        : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(0m),
        new ExtraDamageVar(1m),
        new CalculatedDamageVar(ValueProp.Move).FromOsty().WithMultiplier((CardModel card, Creature _) => DamageOf(card)),
    ];

    /// <summary>
    /// 这一拳的伤害 = 奥斯提缺失的生命值 + 他身上的灾厄层数（不在场就按 0 算）。
    /// </summary>
    public static int DamageOf(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，访问 Owner 会抛 CanonicalModelException
        if (card.IsCanonical) return 0;
        if (card.Owner?.Osty is not { IsAlive: true } osty) return 0;

        int missing = Math.Max(0, osty.MaxHp - osty.CurrentHp);
        int doom = (int)DoomUtil.DoomOf(osty);
        return missing + Math.Max(0, doom);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;
        if (Osty.CheckMissingWithAnim(Owner)) return;
        if (Owner.Osty is not { IsAlive: true } osty) return;

        int damage = DamageOf(this);

        if (damage > 0)
        {
            await DamageCmd.Attack(damage)
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

    /// <summary>
    /// 战斗内才追加「（造成 X 点伤害）」这一行。
    /// 不用 l10n 的 {InCombat:…} 条件：那个变量在图鉴里也会被判成「在战斗中」，
    /// 于是图鉴会显示「（造成 0 点伤害）」。改成代码侧判断（无战斗状态就不加），更稳。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        // 只有「真的在战斗里」才显示这一行：
        // 图鉴、牌堆查看、战斗结算界面都拿不到 IsInProgress，于是这里给空字符串。
        bool inCombat = !IsCanonical
            && Owner?.PlayerCombatState != null
            && CombatManager.Instance.IsInProgress;
        if (!inCombat)
        {
            description.Add("DamageReadout", string.Empty);
            return;
        }

        description.Add("DamageReadout", $"\n[green]（造成{DamageOf(this)}点伤害）[/green]");
    }
}
