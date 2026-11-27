using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using NecroRework.Stats;

namespace NecroRework;

/// <summary>
/// 给「原版卡也能显示战斗内读数」的集中处理。
/// 本地化的 <c>{InCombat:…}</c> 在 0.114 里即使打开百科（没有战斗）也会被判成真，
/// 于是这些括号会在图鉴/牌组里冒出「（获得 0 点格挡）」这种怪东西。
/// 这里统一改成代码侧判断：只有「真的在战斗内的手牌」才写入读数，其余情况写入空串。
/// </summary>
internal static class CombatReadouts
{
    private const string SpurKey = "SpurReadout";
    private const string CleanseKey = "CleanseReadout";
    private const string SacrificeKey = "SacrificeReadout";
    private const string RattleKey = "RattleReadout";
    private const string PullKey = "PullReadout";

    /// <summary>猛晃：每 15 点奥斯提累计伤害多打一次（升级后 14）。</summary>
    private const int RattleThreshold = 15;
    private const int RattleThresholdUpgraded = 14;

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(CardModel), "AddExtraArgsToDescription");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 战斗内读数：找不到 CardModel.AddExtraArgsToDescription，跳过");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(CombatReadouts), nameof(Postfix)));
        Main.Log.Info($"[{Main.ModId}] 战斗内读数已挂载（增生 / 洁净 / 牺牲 / 猛晃 / 亡魂牵引）");
    }

    private static void Postfix(CardModel __instance, LocString description)
    {
        if (__instance is not (Spur or Cleanse or Sacrifice or Rattle or PullFromBelow))
        {
            return;
        }

        try
        {
            Fill(__instance, description);
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 战斗内读数计算失败（已忽略）：{e.Message}");
            Clear(description);
        }
    }

    private static void Clear(LocString description)
    {
        description.Add(SpurKey, string.Empty);
        description.Add(CleanseKey, string.Empty);
        description.Add(SacrificeKey, string.Empty);
        description.Add(RattleKey, string.Empty);
        description.Add(PullKey, string.Empty);
    }

    private static bool IsInHandDuringCombat(CardModel card)
    {
        return !card.IsCanonical
            && card.Owner?.PlayerCombatState != null
            && CombatManager.Instance.IsInProgress
            && card.Pile?.Type == PileType.Hand;
    }

    private static void Fill(CardModel card, LocString description)
    {
        if (!IsInHandDuringCombat(card))
        {
            Clear(description);
            return;
        }

        Player owner = card.Owner!;

        switch (card)
        {
            case Spur:
                description.Add(SpurKey, BlockLine(BlockPreview(card)));
                break;

            case Cleanse:
                description.Add(CleanseKey, BlockLine(BlockPreview(card)));
                break;

            case Sacrifice:
            {
                int block = owner.IsOstyAlive ? BlockPreview(card) : 0;
                description.Add(SacrificeKey, BlockLine(block));
                break;
            }

            case Rattle:
            {
                int threshold = card.IsUpgraded ? RattleThresholdUpgraded : RattleThreshold;
                int hits = 1 + OstyDamageTracker.TotalFor(owner) / threshold;
                description.Add(RattleKey, $"\n[green]（命中{hits}次）[/green]");
                break;
            }

            case PullFromBelow:
            {
                int hits = 0;
                ICombatState? state = owner.Creature?.CombatState;
                if (state != null)
                {
                    foreach (CombatHistoryEntry entry in CombatManager.Instance.History.Entries)
                    {
                        if (entry is CardPlayFinishedEntry played
                            && played.CardPlay.Player == owner
                            && played.WasEthereal)
                        {
                            hits++;
                        }
                    }
                }

                description.Add(PullKey, $"\n[green]（命中{hits}次）[/green]");
                break;
            }
        }
    }

    private static string BlockLine(int block)
    {
        return $"\n[green]（获得{block}点格挡）[/green]";
    }

    /// <summary>读 CalculatedBlock 的当前预览值（不含敏捷/脆弱这类全局修正，够卡面参考用）。</summary>
    private static int BlockPreview(CardModel card)
    {
        if (card.DynamicVars.ContainsKey("CalculatedBlock")
            && card.DynamicVars["CalculatedBlock"] is CalculatedVar calc)
        {
            return (int)Math.Round(calc.Calculate(null), MidpointRounding.AwayFromZero);
        }

        return 0;
    }
}
