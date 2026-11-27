using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
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

    /// <summary>
    /// 计数器只是「紧追不放」的附属显示：本体没挂上（被人工制品挡掉、或者本体到期被移除）时，
    /// 它不能单独留在地上当个不消退的减益。
    /// </summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;

        if (Owner.GetPower<RelentlessPower>() == null)
        {
            await PowerCmd.Remove(this);
        }
    }
}
