using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework;

/// <summary>
/// 灾厄（Doom）相关的标准操作。所有新卡都必须走这里，保证判定次数和触发行为一致。
///
/// 关键规则（依据原版实现）：
///  - 死亡之门(DeathsDoor)：看「本回合由玩家施加过的 DoomPower」→ applier 必须是玩家生物。
///  - 厄运之衣(ShroudPower)：applier == 玩家 → 起甲。
///  - 血肉戏法(SleightOfFleshPower)：只对「敌人身上的减益」触发 → 给自己/奥斯提上灾厄天然不触发。
///  - 纯移除灾厄：applier 传 null，不产生任何“施加”判定，什么都不会触发。
/// </summary>
public static class DoomUtil
{
    public static int DoomOf(Creature creature) => creature?.GetPower<DoomPower>()?.Amount ?? 0;

    public static int PlayerDoom(CardModel card) => DoomOf(card.Owner?.Creature);

    public static int OstyDoom(CardModel card) => DoomOf(card.Owner?.Osty);

    public static int TotalSelfDoom(CardModel card) => PlayerDoom(card) + OstyDoom(card);

    /// <summary>给自己上灾厄（会正常触发死亡之门 / 厄运之衣，不会触发血肉戏法）。</summary>
    public static async Task ApplyToSelf(PlayerChoiceContext ctx, CardModel card, decimal amount)
    {
        if (card.Owner?.Creature is not { } me || amount <= 0m) return;
        await PowerCmd.Apply<DoomPower>(ctx, me, amount, me, card);
    }

    /// <summary>给奥斯提上灾厄（applier 仍是玩家，所以同样触发死亡之门 / 厄运之衣）。</summary>
    public static async Task ApplyToOsty(PlayerChoiceContext ctx, CardModel card, decimal amount)
    {
        if (card.Owner?.Creature is not { } me || card.Owner.Osty is not { IsAlive: true } osty || amount <= 0m) return;
        await PowerCmd.Apply<DoomPower>(ctx, osty, amount, me, card);
    }

    /// <summary>纯移除自己的灾厄：applier = null，不触发任何东西。</summary>
    public static async Task RemoveDoom(PlayerChoiceContext ctx, CardModel card, Creature target, int amount)
    {
        if (amount <= 0) return;
        var power = target.GetPower<DoomPower>();
        if (power == null) return;
        await PowerCmd.ModifyAmount(ctx, power, -amount, null, card);
    }

    /// <summary>
    /// 把角色身上的灾厄转移给奥斯提：只从玩家身上扣，不做奥斯提→玩家方向。
    /// 同样是「移除不判定 + 落点只判定一次」。
    /// 返回实际转移的层数。
    /// </summary>
    public static async Task<int> TransferToOsty(PlayerChoiceContext ctx, CardModel card, decimal max)
    {
        if (card.Owner?.Creature is not { } me || card.Owner.Osty is not { IsAlive: true } osty) return 0;

        var playerPower = me.GetPower<DoomPower>();
        int playerAmt = playerPower?.Amount ?? 0;
        int take = (int)Math.Min(max, playerAmt);
        if (take <= 0) return 0;

        await PowerCmd.ModifyAmount(ctx, playerPower, -take, null, card);
        await PowerCmd.Apply<DoomPower>(ctx, osty, take, me, card);
        return take;
    }

    /// <summary>
    /// 转移灾厄给敌人：先扣角色身上的，不够再扣奥斯提的；最多 max 层。
    /// 移除部分不产生判定，最后对敌人做「一次」施加判定。
    /// 返回实际转移的层数。
    /// </summary>
    public static async Task<int> TransferToEnemy(PlayerChoiceContext ctx, CardModel card, Creature enemy, decimal max)
    {
        if (card.Owner?.Creature is not { } me || enemy == null) return 0;

        var playerPower = me.GetPower<DoomPower>();
        var ostyPower = card.Owner.Osty?.GetPower<DoomPower>();

        int playerAmt = playerPower?.Amount ?? 0;
        int ostyAmt = ostyPower?.Amount ?? 0;
        int take = (int)Math.Min(max, playerAmt + ostyAmt);
        if (take <= 0) return 0;

        int fromPlayer = Math.Min(playerAmt, take);
        int fromOsty = take - fromPlayer;

        if (fromPlayer > 0 && playerPower != null)
            await PowerCmd.ModifyAmount(ctx, playerPower, -fromPlayer, null, card);
        if (fromOsty > 0 && ostyPower != null)
            await PowerCmd.ModifyAmount(ctx, ostyPower, -fromOsty, null, card);

        // 唯一的一次“施加”判定
        await PowerCmd.Apply<DoomPower>(ctx, enemy, take, me, card);
        return take;
    }
}
