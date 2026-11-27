using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Cards;

/// <summary>
/// 飘摇不定：0 费技能（白卡/普通品质）。
/// 弃 2 张牌；然后随机挑一张手牌里的虚无牌，
/// 在它下次被打出之前耗能 -1（reduceOnly，已经是 0 费就不会变成负数）。
/// 升级后获得「保留」。
/// </summary>
public sealed class Wavering : CardModel
{
    /// <summary>一次弃几张（卡面与选牌提示共用）。</summary>
    private const int DiscardCount = 2;

    public Wavering()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    // 卡面提到「虚无」和「耗能」，两个词条都挂出来。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
        HoverTipFactory.ForEnergy(this)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        var prefs = new CardSelectorPrefs(
            new LocString("cards", "WAVERING.selectionScreenPrompt"), DiscardCount);

        var picked = await CardSelectCmd.FromHand(
            choiceContext, Owner, prefs,
            c => true,
            this);

        var toDiscard = picked.Take(DiscardCount).ToList();
        if (toDiscard.Count > 0)
        {
            await CardCmd.Discard(choiceContext, toDiscard);
        }

        // 弃完之后再挑虚无牌：剩下的手牌里随机一张。
        var hand = PileType.Hand.GetPile(Owner);
        var candidates = hand == null
            ? []
            : hand.Cards.Where(c => c.Keywords.Contains(CardKeyword.Ethereal)).ToList();
        if (candidates.Count == 0)
        {
            Main.Log.Info($"[{Main.ModId}] 飘摇不定：手牌里没有虚无牌，跳过减费");
            return;
        }

        var target = Owner.RunState.Rng.CombatCardSelection.NextItem(candidates);
        if (target == null) return;

        target.EnergyCost.AddUntilPlayed(-1, reduceOnly: true);
        target.InvokeEnergyCostChanged();
        Main.Log.Info($"[{Main.ModId}] 飘摇不定：{target.Id} 在下次打出前耗能 -1");
    }

    protected override void OnUpgrade()
    {
        // 升级后获得「保留」，不再因回合结束被弃掉。
        AddKeyword(CardKeyword.Retain);
    }
}
