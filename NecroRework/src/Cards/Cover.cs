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
/// 掩护：2 费攻击（罕见/蓝卡）。
/// 奥斯提造成 9（13）点伤害 → 他吸收你至多 15（20）层灾厄 → 为你提供 5（7）点格挡。
/// </summary>
public sealed class Cover : CardModel
{
    private const decimal OstyDamage = 9m;

    private const decimal DoomAbsorb = 20m;

    private const decimal Block = 5m;

    public Cover()
        : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new OstyDamageVar(OstyDamage, ValueProp.Move),
        new PowerVar<DoomPower>(DoomAbsorb),
        new BlockVar(Block, ValueProp.Move),
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

        // 奥斯提吸收你至多 N 层灾厄（不足则全吸收），算作「你施加给奥斯提」。
        await DoomUtil.TransferToOsty(choiceContext, this, DynamicVars.Doom.BaseValue);

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.OstyDamage.UpgradeValueBy(4m);
        DynamicVars.Doom.UpgradeValueBy(5m);
        DynamicVars.Block.UpgradeValueBy(2m);
    }
}
