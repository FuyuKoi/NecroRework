using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 掩护：2 费攻击（罕见/蓝卡）。
/// 奥斯提造成 9 点伤害 → 召唤 7（11） → 你获得等同于「召唤之后他的生命值」的格挡。
/// （先打伤害再召唤，所以格挡是「原来的血 + 这次召唤涨的血」。）
/// </summary>
public sealed class Cover : CardModel
{
    private const decimal OstyDamage = 9m;

    private const decimal Summon = 7m;

    private const decimal SummonUpgrade = 4m;

    public Cover()
        : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new OstyDamageVar(OstyDamage, ValueProp.Move),
        new SummonVar(Summon),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;
        if (Osty.CheckMissingWithAnim(Owner)) return;
        if (Owner.Osty is not { IsAlive: true } osty) return;

        await DamageCmd.Attack(DynamicVars.OstyDamage.BaseValue)
            .FromOsty(osty, this, cardPlay)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
            .Execute(choiceContext);

        await OstyCmd.Summon(choiceContext, Owner, DynamicVars.Summon.BaseValue, this);

        // 召唤之后奥斯提的生命值就是这次的格挡值（召唤会同时抬高上限与当前血量）。
        if (Owner.Osty is not { IsAlive: true } grown) return;
        int block = grown.CurrentHp;
        if (block <= 0) return;

        await CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Move, cardPlay);
        Main.Log.Info($"[{Main.ModId}] 掩护：奥斯提当前生命 {block} → 你获得 {block} 点格挡");
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Summon.UpgradeValueBy(SummonUpgrade);
    }

    /// <summary>
    /// 「（获得 X 点格挡）」只在战斗中出现（图鉴/牌堆里不显示）。
    /// 不用本地化的 {InCombat:…}：0.114 里那个条件在图鉴也会被判真。
    /// 预览值 = 奥斯提当前生命 + 这次召唤要加的生命（召唤会同时抬上限与当前血量）。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        bool inCombat = !IsCanonical && Owner?.PlayerCombatState != null && CombatManager.Instance.IsInProgress;
        if (!inCombat)
        {
            description.Add("BlockReadout", string.Empty);
            return;
        }

        description.Add("BlockReadout", $"\n[green]（获得{BlockPreview()}点格挡）[/green]");
    }

    private int BlockPreview()
    {
        if (Owner?.Osty is not { IsAlive: true } osty) return 0;
        return osty.CurrentHp + (int)DynamicVars.Summon.BaseValue;
    }
}
