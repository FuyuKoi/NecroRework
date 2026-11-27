using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using NecroRework.Enchantments;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 万事皆空：1 费技能（稀有品质），虚无。
/// 机制同原版「添柴」（把整只手烧掉再补等量的牌），但补出来的牌是「反相」的：
///   · 每消耗一张「非虚无」牌 → 生成一张随机的「虚无」牌；
///   · 每消耗一张「虚无」牌 → 生成一张随机的「非虚无」牌，并附魔「飘荡」。
/// 允许生成重复的牌；升级后生成出来的牌也是升级过的。
/// </summary>
public sealed class AllIsVoid : CardModel
{
    public AllIsVoid()
        : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    // 卡面写「飘荡」，挂附魔词条。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromEnchantment<DriftEnchantment>();

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        // 先把手上的牌全部快照下来：消耗过程中手牌会变，直接遍历会出问题。
        List<CardModel> handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (handCards.Count == 0)
        {
            return;
        }

        int etherealCount = handCards.Count(c => c.Keywords.Contains(CardKeyword.Ethereal));
        int normalCount = handCards.Count - etherealCount;

        // 全部消耗掉（带的「飘荡」在这里也会照常结算）。
        // 单张失败（比如亡魂牵引把它改成自动打出、而它没有合法目标）不能中断整张牌，
        // 否则会出现「手牌被烧光了、却一张反相牌都没生成」的情况。
        foreach (CardModel card in handCards)
        {
            try
            {
                await CardCmd.Exhaust(choiceContext, card);
            }
            catch (System.Exception e)
            {
                Main.Log.Error($"[{Main.ModId}] 万事皆空：消耗 {card.Id} 失败（已跳过）: {e.Message}");
            }
        }

        // 随机池：只用本角色的卡池，虚无 / 非虚无分开抽；允许重复（同「添柴」）。
        List<CardModel> pool = Owner.Character.CardPool
            .GetUnlockedCards(Owner.UnlockState, Owner.RunState.CardMultiplayerConstraint)
            .ToList();

        List<CardModel> fromEtherealPool = pool
            .Where(c => c.CanonicalKeywords.Contains(CardKeyword.Ethereal))
            .ToList();

        List<CardModel> fromNormalPool = pool
            .Where(c => !c.CanonicalKeywords.Contains(CardKeyword.Ethereal))
            .ToList();

        var generated = new List<CardModel>();

        // 兜底：如果目标池子空了（比如卡池里没有足够的虚无牌），就拿另一半池子补，
        // 免得「手牌烧光却一张都不生成」。
        List<CardModel> EtherealSource() =>
            fromEtherealPool.Count > 0 ? fromEtherealPool : fromNormalPool;

        List<CardModel> NormalSource() =>
            fromNormalPool.Count > 0 ? fromNormalPool : fromEtherealPool;

        // 消耗的是非虚无牌 → 补虚无牌。
        if (normalCount > 0)
        {
            List<CardModel> source = EtherealSource();
            if (source.Count > 0)
            {
                generated.AddRange(CardFactory.GetForCombat(
                    Owner, source, normalCount, Owner.RunState.Rng.CombatCardGeneration));
            }
        }

        // 消耗的是虚无牌 → 补非虚无牌（稍后附魔「飘荡」）。
        int filledWithNormal = 0;
        if (etherealCount > 0)
        {
            List<CardModel> source = NormalSource();
            if (source.Count > 0)
            {
                List<CardModel> normals = CardFactory.GetForCombat(
                    Owner, source, etherealCount, Owner.RunState.Rng.CombatCardGeneration).ToList();
                generated.AddRange(normals);
                filledWithNormal = normals.Count;
            }
        }

        if (generated.Count == 0)
        {
            return;
        }

        // 升级：生成出来的牌也一起升级（放在附魔之前，免得附魔后的卡没法升级）。
        if (IsUpgraded)
        {
            CardCmd.Upgrade(generated, CardPreviewStyle.None);
        }

        // 虚无换来的那几张附魔「飘荡」。
        if (filledWithNormal > 0)
        {
            for (int i = generated.Count - filledWithNormal; i < generated.Count; i++)
            {
                CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), generated[i], 1m);
            }
        }

        await CardPileCmd.AddGeneratedCardsToCombat(generated, PileType.Hand, Owner);
    }
}
