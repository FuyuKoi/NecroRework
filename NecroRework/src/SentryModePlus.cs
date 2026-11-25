using System;
using System.Collections.Generic;
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
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

using NecroRework.Settings;
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

        // 能力图标上的悬停预览也换成「扫荡凝视+」（场上那一层能力如果是升级版打出来的）
        MethodInfo? powerTips = AccessTools.PropertyGetter(typeof(SentryModePower), "ExtraHoverTips");
        if (powerTips != null)
        {
            harmony.Patch(powerTips,
                postfix: new HarmonyMethod(typeof(SentryModePlus), nameof(PowerTipsPostfix)));
        }

        Main.Log.Info($"[{Main.ModId}] 哨卫模式强化已挂载（能力悬停={powerTips != null}）");
    }

    private static void PowerTipsPostfix(SentryModePower __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!NecroSettings.CardReworks) return;

        if (!IsMarked(__instance)) return;

        try
        {
            __result = [HoverTipFactory.FromCard<SweepingGaze>(upgrade: true)];
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 哨卫模式悬停预览替换失败: {e.Message}");
        }
    }

    private static bool BeforeHandDrawPrefix(SentryModePower __instance, Player player,
        PlayerChoiceContext choiceContext, ICombatState combatState, ref Task __result)
    {
        if (!NecroSettings.CardReworks) return true;

        if (!IsMarked(__instance)) return true;

        // 兜底：这里一旦抛异常，会把「抽牌阶」整个弄死（表现为回合开始时卡住）。
        // 所以最外层再包一层，出问题就退回原版逻辑（生成普通扫荡凝视）而不是让回合死掉。
        try
        {
            __result = GenerateUpgraded(__instance, player, combatState);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 哨卫模式强化执行失败，退回原版生成逻辑: {e}");
            return true;
        }
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
