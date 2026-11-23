using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 负世：2（1）费能力，稀有，多人专属。
/// 奥斯提为所有玩家承受未被格挡的伤害与非攻击伤害。
/// </summary>
public sealed class WorldBurden : CardModel
{
    public WorldBurden()
        : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    /// <summary>只会在多人游戏里出现。</summary>
    public override CardMultiplayerConstraint MultiplayerConstraint =>
        CardMultiplayerConstraint.MultiplayerOnly;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);
        await PowerCmd.Apply<WorldBurdenPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        // 升级只降费：2 → 1。
        EnergyCost.UpgradeBy(-1);
    }
}
