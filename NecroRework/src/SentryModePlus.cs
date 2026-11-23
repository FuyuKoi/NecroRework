using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework;

/// <summary>
/// 哨卫模式的升级效果：敲过之后，每回合塞进手牌的「扫荡凝视」是升级版。
///
/// 原版的能力本身不带「我是不是升级版打出来的」信息，所以这里在打出时给那一层能力
/// 打了个弱引用标记，然后拦在 SentryModePower.BeforeHandDraw 前面自己生成升级版。
/// </summary>
internal static class SentryModePlus
{
    private static readonly ConditionalWeakTable<PowerModel, object> Upgraded = new();

    internal static void Mark(PowerModel power)
    {
        try
        {
            if (!Upgraded.TryGetValue(power, out _))
            {
                Upgraded.Add(power, new object());
            }
        }
        catch
        {
            // 标记失败只会退化成普通扫荡凝视，不影响游戏
        }
    }

    internal static bool IsMarked(PowerModel power)
    {
        try
        {
            return Upgraded.TryGetValue(power, out _);
        }
        catch
        {
            return false;
        }
    }

    public static void Install(Harmony harmony)
    {
        MethodInfo? target = AccessTools.Method(typeof(SentryModePower), nameof(SentryModePower.BeforeHandDraw),
            [typeof(Player), typeof(PlayerChoiceContext), typeof(ICombatState)]);

        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 哨卫模式强化：找不到 SentryModePower.BeforeHandDraw");
            return;
        }

        harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(SentryModePlus), nameof(BeforeHandDrawPrefix)));

        Main.Log.Info($"[{Main.ModId}] 哨卫模式强化已挂载");
    }

    private static bool BeforeHandDrawPrefix(SentryModePower __instance, Player player,
        PlayerChoiceContext choiceContext, ICombatState combatState, ref Task __result)
    {
        if (!IsMarked(__instance)) return true;

        __result = GenerateUpgraded(__instance, player, combatState);
        return false;
    }

    private static async Task GenerateUpgraded(SentryModePower power, Player player, ICombatState combatState)
    {
        try
        {
            Player? owner = power.Owner.Player;
            if (owner == null || player != owner) return;

            int count = power.Amount;
            for (int i = 0; i < count; i++)
            {
                CardModel gaze = combatState.CreateCard<SweepingGaze>(owner);
                gaze.UpgradeInternal();
                await CardPileCmd.AddGeneratedCardToCombat(gaze, PileType.Hand, owner);
            }
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 哨卫模式生成升级版扫荡凝视失败: {e.Message}");
        }
    }
}
