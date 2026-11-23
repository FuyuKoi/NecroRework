using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 造灵：1 费技能（普通品质，升级后 0 费）。
/// 奥斯提存活时：他失去 15 点生命值、承受你的 3 点灾厄，你手牌里入手一张灵魂（升级后是灵魂+）。
/// 如果施放时他身上已经有灾厄，他再失去 4 点生命值和 20 点生命值上限。
/// </summary>
public sealed class CraftSpirit : CardModel
{
    private const ValueProp HpLossProps = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    private const decimal OstyHpLoss = 11m;
    private const decimal DoomToOsty = 9m;
    private const decimal ExtraHpLoss = 4m;
    private const decimal MaxHpLoss = 20m;

    public CraftSpirit()
        : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    /// <summary>奥斯提不在场时这张牌毫无作用，直接按原版「奥斯提牌」的规矩禁打并标红。</summary>
    // 百科/图鉴里拿到的是 canonical 模型，访问 Owner 会抛 CanonicalModelException，先短路
    protected override bool IsPlayable => IsCanonical || !Owner.IsOstyMissing;

    protected override bool ShouldGlowRedInternal => !IsCanonical && Owner.IsOstyMissing;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner.Osty is not { IsAlive: true } osty) return;

        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        // 「释放时他已经有灾厄」——按施放瞬间判定，后面的伤害不影响这个条件。
        var hadDoom = DoomUtil.DoomOf(osty) > 0;

        await CreatureCmd.Damage(choiceContext, osty, OstyHpLoss, HpLossProps, this, cardPlay);

        if (osty.IsAlive)
        {
            // 他得到你的 3 点灾厄：从角色身上扣，落到奥斯提身上只算一次施加。
            await DoomUtil.TransferToOsty(choiceContext, this, DoomToOsty);

            if (hadDoom)
            {
                await CreatureCmd.Damage(choiceContext, osty, ExtraHpLoss, HpLossProps, this, cardPlay);

                if (osty.IsAlive)
                    await CreatureCmd.LoseMaxHp(choiceContext, osty, MaxHpLoss, this);
            }
        }

        var souls = Soul.Create(Owner, 1, CombatState);
        if (IsUpgraded)
        {
            foreach (var soul in souls)
                CardCmd.Upgrade(soul);
        }

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
            souls, PileType.Hand, Owner));
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
