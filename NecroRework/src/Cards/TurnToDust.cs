using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NecroRework.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 化作尘泥：1 费蓝色能力。下个回合开始前，剩余格挡的一半转化为等量召唤。
/// 升级只加「保留」（费用不变）。
/// </summary>
public sealed class TurnToDust : CardModel
{
    public TurnToDust() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    // 卡面写「格挡」「召唤」，挂这两个词条。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block),
        HoverTipFactory.Static(StaticHoverTip.SummonStatic)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay play)
    {
        try
        {
            await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);

            if (!Owner.Creature.HasPower<TurnToDustPower>())
            {
                await PowerCmd.Apply<TurnToDustPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
            }
        }
        catch (Exception e)
        {
            Main.Log.Info($"化作尘泥 出牌异常：{e}");
        }
    }

    protected override void OnUpgrade()
    {
        // 升级：获得「保留」。
        AddKeyword(CardKeyword.Retain);
    }
}
