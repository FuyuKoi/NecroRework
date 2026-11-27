using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 此生渺渺：每回合打出的第 6 张「虚无」牌，会让你获得 1 层无实体。
/// </summary>
public sealed class FleetingLife : CardModel
{
    public FleetingLife()
        : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    // 卡面写「无实体」，挂这个词条（虚无本身是卡的关键词，引擎会自动加）。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<IntangiblePower>()];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);

        if (Owner.Creature.GetPower<FleetingLifePower>() == null)
        {
            await PowerCmd.Apply<FleetingLifePower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
