using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 初心（罕见能力）：生成 3 张「找回」到手牌；每回合开始时把上回合最先被动消耗的
/// 3 张虚无牌放回手牌，并获得 6 点格挡。升级后获得「固有」。
/// </summary>
public sealed class BeginnerMind : CardModel
{
    private const int RetrievalCount = 3;

    public BeginnerMind()
        : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    // 悬停时把「找回」的卡面也显示出来（跟猎人的刀刃之舞显示小刀一个原理）
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromCard<Retrieve>()];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "PowerUp", Owner.Character.PowerUpAnimDelay);

        if (Owner.Creature.GetPower<BeginnerMindPower>() == null)
        {
            await PowerCmd.Apply<BeginnerMindPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
        }

        var retrievals = new List<CardModel>(RetrievalCount);
        for (int i = 0; i < RetrievalCount; i++)
        {
            // 必须走 CombatState.CreateCard，这样生成的牌才有 Owner（否则加手牌会失败）
            retrievals.Add(CombatState.CreateCard<Retrieve>(Owner));
        }

        await CardPileCmd.AddGeneratedCardsToCombat(retrievals, PileType.Hand, Owner);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }
}
