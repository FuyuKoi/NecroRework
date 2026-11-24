using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 造灵：1 费技能（普通品质，升级后 0 费）。
/// 奥斯提存活时：他失去 7 点生命值、承受你的 9 点灾厄，你手牌里入手一张灵魂（升级后是灵魂+）。
/// </summary>
public sealed class CraftSpirit : CardModel
{
    private const ValueProp HpLossProps = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    private const decimal OstyHpLoss = 7m;
    private const decimal DoomToOsty = 9m;

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

        // 只掉血、只转移灾厄；「已有灾厄时的额外惩罚」那一段已按需求去掉。
        await CreatureCmd.Damage(choiceContext, osty, OstyHpLoss, HpLossProps, this, cardPlay);

        if (osty.IsAlive)
        {
            // 他得到你的 3 点灾厄：从角色身上扣，落到奥斯提身上只算一次施加。
            await DoomUtil.TransferToOsty(choiceContext, this, DoomToOsty);
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
