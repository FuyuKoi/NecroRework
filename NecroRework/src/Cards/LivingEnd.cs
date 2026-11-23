using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 生灵之末：3 费（升级 2 费）稀有能力牌，虚无。对自己施加「恶咒」。
/// 恶咒（HexPower）就是幽灵骑士（SpectralKnight）给玩家的那个减益：
/// 它会给战斗中的所有牌附上「咒印」（Hexed）→ 那些牌获得虚无。
/// 层数与幽灵骑士一致用 2。
/// </summary>
public sealed class LivingEnd : CardModel
{
    /// <summary>与幽灵骑士（SpectralKnight.HexMove）施加的层数一致。</summary>
    private const decimal HexAmount = 2m;

    public LivingEnd()
        : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<HexPower>(2)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);

        await PowerCmd.Apply<HexPower>(choiceContext, Owner.Creature, HexAmount, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
