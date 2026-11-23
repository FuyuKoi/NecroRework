using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 相忘于川：1（0）费能力，稀有，虚无。
/// 奥斯提为你承受所有的灾厄与非攻击伤害。
/// </summary>
public sealed class RiverOfOblivion : CardModel
{
    public RiverOfOblivion()
        : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);
        await PowerCmd.Apply<RiverOfOblivionPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        // 升级只降费：1 → 0。
        EnergyCost.UpgradeBy(-1);
    }
}
