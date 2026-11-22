using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Powers;

/// <summary>
/// 命运同担的附加效果：本场战斗中，每当有敌人获得力量，奥斯提也获得等量力量。
/// 由 <see cref="SharedFateRework"/> 在 <c>Hook.AfterPowerAmountChanged</c> 里驱动。
/// </summary>
public sealed class SharedFatePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override int DisplayAmount => Amount;
}
