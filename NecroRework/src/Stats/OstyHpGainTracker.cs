using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace NecroRework.Stats;

/// <summary>
/// 记录「本回合内奥斯提增加了多少生命值」。
/// 原版只在奥斯提身上结算，也不保留这个数字，所以这里挂 Hook.AfterCurrentHpChanged 自己累加。
/// 只算当前生命值的正向变化（治疗、召唤回血），不算生命上限的变化；玩家回合开始时清零。
/// </summary>
internal static class OstyHpGainTracker
{
    private static readonly Dictionary<Player, decimal> Gained = new();

    public static void Install(Harmony harmony)
    {
        var currentHpChanged = AccessTools.Method(typeof(Hook), "AfterCurrentHpChanged");
        if (currentHpChanged == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterCurrentHpChanged，增生的格挡会恒为 0");
            return;
        }

        harmony.Patch(currentHpChanged,
            postfix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(AfterCurrentHpChangedPostfix)));

        var turnStart = AccessTools.Method(typeof(Hook), "AfterPlayerTurnStart");
        if (turnStart == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.AfterPlayerTurnStart，奥斯提回血量不会按回合清零");
        }
        else
        {
            harmony.Patch(turnStart,
                postfix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(AfterPlayerTurnStartPostfix)));
        }
    }

    /// <summary>玩家本回合内，奥斯提一共增加了多少点当前生命值。</summary>
    public static decimal GainedThisTurn(Player player) =>
        player != null && Gained.TryGetValue(player, out var amount) ? amount : 0m;

    private static void AfterCurrentHpChangedPostfix(Creature creature, decimal delta)
    {
        try
        {
            if (creature?.Monster is not Osty) return;
            if (delta <= 0m) return;
            if (creature.PetOwner is not { } owner) return;

            Gained.TryGetValue(owner, out var current);
            Gained[owner] = current + delta;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 记录奥斯提回血量失败: {e}");
        }
    }

    private static void AfterPlayerTurnStartPostfix(Player player)
    {
        if (player == null) return;
        Gained.Remove(player);
    }
}
