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

        // 注意：清零必须发生在「回合开始的各种遗物效果之前」。
        // 起始遗物「不灭命匣」（PhylacteryUnbound）在 Hook.AfterSideTurnStart 里自动召唤，
        // 而那个钩子早于 Hook.AfterPlayerTurnStart —— 所以旧写法（挂在 AfterPlayerTurnStart 清零）
        // 会把遗物刚加的那几点血一起抹掉，增生就吃不到它。
        // 改成挂 BeforeSideTurnStart（同一回合更早的位置）清零。
        var turnStart = AccessTools.Method(typeof(Hook), "BeforeSideTurnStart",
            [typeof(MegaCrit.Sts2.Core.Combat.ICombatState), typeof(MegaCrit.Sts2.Core.Combat.CombatSide),
             typeof(IReadOnlyList<Creature>)]);

        if (turnStart == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.BeforeSideTurnStart，奥斯提回血量不会按回合清零");
        }
        else
        {
            // 用 prefix 清零：必须在「回合开始时的各种结算」之前清空，
            // 这样下面这些在同一钩子里发生的加血才会算进本回合：
            //   · 化作尘泥（TurnToDustPower）把剩余格挡转成召唤
            //   · 遗物 / 其它 mod 在回合开始时的召唤
            // 以前挂在 postfix（结算完之后清），会把它们刚加的血抹掉，增生就吃不到。
            harmony.Patch(turnStart,
                prefix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(BeforeSideTurnStartPrefix)));
        }

        // 每场战斗开始时把账本清空：上一场战斗的累计不能带进新战斗。
        // （必须在「战斗开始时召唤」之前清，所以我们挂在 BeforeCombatStart 上。）
        var combatStart = AccessTools.Method(typeof(Hook), "BeforeCombatStart");

        if (combatStart == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 Hook.BeforeCombatStart，奥斯提回血量可能跨战斗累积");
        }
        else
        {
            // 用 prefix：必须在「战斗开始时召唤」那些监听者之前清，
            // 否则它们刚加的血会被我们抹掉。
            harmony.Patch(combatStart,
                prefix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(BeforeCombatStartPrefix)));
        }

        // 旧写法（回合开始遗物结算之后才清零，会把自动召唤加的血抹掉），已停用：
        // var legacyTurnStart = AccessTools.Method(typeof(Hook), "AfterPlayerTurnStart");
        // if (legacyTurnStart != null)
        //     harmony.Patch(legacyTurnStart,
        //         postfix: new HarmonyMethod(typeof(OstyHpGainTracker), nameof(AfterPlayerTurnStartPostfix)));
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

    private static void BeforeSideTurnStartPrefix(MegaCrit.Sts2.Core.Combat.CombatSide side)
    {
        // 玩家回合刚要开始（此时遗物/化作尘泥等还没结算）：把上一回合的累计清掉。
        //
        // 但每场战斗的第一个玩家回合要跳过清理 —— 开头那些「战斗开始时召唤」
        // （缚魂命匣 / 无界命匣的 BeforeCombatStart 召唤）已经先把奥斯提血量加上去了，
        // 那是第一回合就吃到增生的操作，不能被清掉。
        if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player) return;

        if (!_keptFirstTurn)
        {
            _keptFirstTurn = true;
            return;
        }

        Gained.Clear();
    }

    /// <summary>本场战斗的第一个玩家回合是否已经「保留」过（不清零）。</summary>
    private static bool _keptFirstTurn;

    private static void BeforeCombatStartPrefix()
    {
        // 新一场战斗：场上血量记录换了一套，全部重新开始。
        Gained.Clear();
        PreHealHp.Clear();
        _keptFirstTurn = false;
    }

    private static void AfterPlayerTurnStartPostfix(Player player)
    {
        if (player == null) return;
        Gained.Remove(player);
    }
}
