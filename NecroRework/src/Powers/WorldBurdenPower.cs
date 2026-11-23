using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 负世（多人专属）的持续效果：其他玩家身上未被格挡的伤害（含攻击与非攻击）
/// 全部改由你的奥斯提承受。你自己那份仍然走「为你而死」的 50% 分摊。
/// </summary>
public sealed class WorldBurdenPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props,
        Creature? dealer)
    {
        Player? owner = Owner.Player;
        if (owner == null) return target;
        if (owner.Osty is not { IsAlive: true } osty) return target;

        // 自己那份交给「为你而死」，这里只接别人的
        if (target == owner.Creature) return target;

        // 只处理玩家角色（宠物、召唤物不在此列）
        if (target.Player == null) return target;

        return osty;
    }
}
