using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Powers;

/// <summary>
/// 紧追不放的计数器：显示「还要再打几下才会触发下一次易伤」。
/// 数字由 <see cref="RelentlessPower"/> 在奥斯提每次命中后同步，本身不做判定。
/// 图标借用遗物「异蛇之眼？？？」的问号，一眼就能看出是计数。
/// </summary>
public sealed class SicEmCounterPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int DisplayAmount => Amount;
}
