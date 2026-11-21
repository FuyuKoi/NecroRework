using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace NecroRework.Stats;

/// <summary>
/// 记录「本回合内奥斯提实际增加了多少生命值」。
/// 只算真正落地的回血（治疗、召唤回血），不算生命上限变化；玩家回合开始时清零。
///
/// 注意：不能挂在 Hook.AfterCurrentHpChanged 上——原版 Heal 传的是「请求的治疗量」，
/// 血满时会溢出（15/17 治 8 也报 8），增生就会按 8 起防。
/// 所以这里改成包住 CreatureCmd.Heal，用「治前血量 / 治后血量」的差值来算真实回血量。
/// </summary>
internal static class OstyHpGainTracker
{
    private static readonly Dictionary<Player, decimal> Gained = new();

    /// <summary>治疗开始前的血量，prefix 写、postfix 读。</summary>
    private static readonly Dictionary<Creature, int> PreHealHp = new();

    public static void Install(Harmony harmony)
    {
        var heal = AccessTools.Method(typeof(CreatureCmd), "Heal",
            [typeof(Creature), typeof(decimal), typeof(bool)]);

        if (heal == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 CreatureCmd.Heal，增生的格挡会恒为 0");
            return;
        }

        harmony.Patch(heal,
            prefix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(HealPrefix)),
            postfix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(HealPostfix)));

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

    private static void HealPrefix(Creature creature)
    {
        try
        {
            if (creature?.Monster is not Osty) return;
            PreHealHp[creature] = creature.CurrentHp;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 记录奥斯提治疗前血量失败: {e}");
        }
    }

    private static void HealPostfix(Creature creature)
    {
        try
        {
            if (creature?.Monster is not Osty) return;
            if (!PreHealHp.Remove(creature, out var before)) return;
            if (creature.PetOwner is not { } owner) return;

            // 只算真正加上去的部分；血满时溢出多少都不算。
            decimal actual = creature.CurrentHp - before;
            if (actual <= 0m) return;

            Gained.TryGetValue(owner, out var current);
            Gained[owner] = current + actual;
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
