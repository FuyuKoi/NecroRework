using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 「捕捉灵魂」追加的临时减力量：本回合内目标失去 N 点力量，回合结束自动还回去。
/// 复用原版 <see cref="TemporaryStrengthPower"/> 的整套机制，只是把来源牌换成捕捉灵魂，
/// 这样悬停时显示的名字与描述都是这张卡自己的。
/// </summary>
public sealed class CaptureSpiritStrengthDownPower : TemporaryStrengthPower
{
    public override AbstractModel OriginModel => ModelDb.Card<CaptureSpirit>();

    protected override bool IsPositive => false;
}
