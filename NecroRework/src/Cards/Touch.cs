using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 触碰：2 费攻击（普通品质，升级后 1 费）。
/// 奥斯提吸收你 50% 的灾厄 → 奥斯提失去 6 点生命值 → 奥斯提造成 6 点伤害 → 你获得 11 点格挡。
/// 奥斯提如果死在这 4 点生命值上，后续伤害与格挡都不会发生。
/// </summary>
public sealed class Touch : CardModel
{
    /// <summary>「失去生命值」的标准写法：不吃格挡、不吃力量，但仍算一次受到伤害。</summary>
    private const ValueProp HpLossProps = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    private const decimal OstyHpLoss = 6m;

    public Touch()
        : base(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(6m, ValueProp.Move), new BlockVar(11m, ValueProp.Move)];

    /// <summary>
    /// 没有奥斯提、或奥斯提撑不过这 6 点生命值时，和原版「奥斯提攻击牌」一样标红，
    /// 提醒玩家这一下只会把奥斯提送走。
    /// </summary>
    protected override bool ShouldGlowRedInternal =>
        !IsCanonical
        && (Owner.IsOstyMissing || (Owner.Osty is { IsAlive: true } osty && osty.CurrentHp <= OstyHpLoss));

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target is not { } target) return;
        if (Owner.Osty is not { IsAlive: true } osty) return;

        // 奥斯提吸收你「50%」的灾厄：向上取整，扣减不判定，落到奥斯提身上算一次施加。
        var share = (int)Math.Ceiling(DoomUtil.PlayerDoom(this) * 0.5m);
        if (share > 0)
            await DoomUtil.TransferToOsty(choiceContext, this, share);

        if (osty.IsAlive)
            await CreatureCmd.Damage(choiceContext, osty, OstyHpLoss, HpLossProps, this, cardPlay);

        // 死于这 6 点生命值：后续伤害与格挡都不再结算。
        if (!osty.IsAlive) return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromOsty(osty, this, cardPlay)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
            .Execute(choiceContext);

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
