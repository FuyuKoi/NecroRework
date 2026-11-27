using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 不详重拳：3 费攻击（罕见/蓝卡），虚无 + 消耗；升级后只去掉「消耗」。
/// 奥斯提造成 30 点伤害 → 你获得 20 点格挡 → 奥斯提获得 10 层灾厄。
/// </summary>
public sealed class OminousPunch : CardModel
{
    private const decimal OstyDamage = 30m;

    private const decimal Block = 20m;

    private const decimal DoomToOsty = 10m;

    public OminousPunch()
        : base(3, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new OstyDamageVar(OstyDamage, ValueProp.Move), new BlockVar(Block, ValueProp.Move)];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Ethereal, CardKeyword.Exhaust];

    // 卡面写「格挡」「灾厄」，把这两个词条挂到卡旁边。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block),
        HoverTipFactory.FromPower<DoomPower>((int)DoomToOsty)
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

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 第三段：奥斯提获得灾厄（原设计）。
        await DoomUtil.ApplyToOsty(choiceContext, this, DoomToOsty);
    }

    protected override void OnUpgrade()
    {
        // 升级只去掉「消耗」，数值（30/20/10）不变。
        RemoveKeyword(CardKeyword.Exhaust);
    }
}
