using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Powers;

/// <summary>
/// 亡魂牵引：你下一次消耗「虚无」牌时，改为自动打出该牌（之后照常进入弃牌堆）。
/// 每层挡一次消耗，挡完就掉一层。
///
/// 真正的拦截逻辑在 <see cref="PullFromBelowRedirect"/> 里（它打的是 CardCmd.Exhaust），
/// 这里只负责「被打出的这张牌的结果位置改成弃牌堆」，这样即使牌上本身带「消耗」也不会进消耗堆。
/// </summary>
public sealed class PullFromBelowPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (!isAutoPlay) return cardLocation;
        if (!ReferenceEquals(card, PullFromBelowRedirect.CurrentCard)) return cardLocation;

        // 能力牌跟手动打出时一样：只生效、不进弃牌堆（原版结果堆是「无」，打出后直接移出战斗）。
        if (card.Type == CardType.Power) return cardLocation;

        return new CardLocation(card.Owner, PileType.Discard, CardPilePosition.Bottom);
    }

    /// <summary>Flash() 在基类里是 protected，外面调不到，包一层。</summary>
    public void FlashNow() => Flash();
}
