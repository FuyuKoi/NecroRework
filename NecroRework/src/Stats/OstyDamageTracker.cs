using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Stats;

/// <summary>
/// 记录「本场战斗中奥斯提累计造成的伤害」（含被格挡的部分），供猛晃这类卡按累计值结算。
/// 每场战斗开始时清零。
/// </summary>
internal static class OstyDamageTracker
{
    private static readonly Dictionary<Player, int> Totals = new();

    public static void Install(Harmony harmony)
    {
        MethodInfo? damageGiven = AccessTools.Method(typeof(Hook), nameof(Hook.AfterDamageGiven),
        [
            typeof(PlayerChoiceContext), typeof(ICombatState), typeof(Creature), typeof(DamageResult),
            typeof(ValueProp), typeof(Creature), typeof(CardModel)
        ]);

        if (damageGiven == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 奥斯提伤害统计：找不到 Hook.AfterDamageGiven");
        }
        else
        {
            harmony.Patch(damageGiven,
                postfix: new HarmonyMethod(typeof(OstyDamageTracker), nameof(AfterDamageGivenPostfix)));
        }

        MethodInfo? combatStart = AccessTools.Method(typeof(Hook), nameof(Hook.BeforeCombatStart),
            [typeof(IRunState), typeof(ICombatState)]);

        if (combatStart == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 奥斯提伤害统计：找不到 Hook.BeforeCombatStart");
        }
        else
        {
            harmony.Patch(combatStart,
                postfix: new HarmonyMethod(typeof(OstyDamageTracker), nameof(BeforeCombatStartPostfix)));
        }
    }

    /// <summary>本场战斗中奥斯提累计造成的伤害。</summary>
    public static int TotalFor(Player? player)
    {
        if (player == null) return 0;

        return Totals.TryGetValue(player, out int total) ? total : 0;
    }

    private static void AfterDamageGivenPostfix(Creature? dealer, DamageResult results)
    {
        try
        {
            if (dealer?.Monster is not Osty osty) return;

            Player? owner = osty.Creature.PetOwner;
            if (owner == null) return;

            int dealt = results.TotalDamage;
            if (dealt <= 0) return;

            Totals[owner] = TotalFor(owner) + dealt;
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 奥斯提伤害统计失败: {e}");
        }
    }

    private static void BeforeCombatStartPostfix()
    {
        Totals.Clear();
    }
}
