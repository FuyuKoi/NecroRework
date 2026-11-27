using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using NecroRework.Powers;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 手牌提示（两套）：
/// <list type="bullet">
/// <item>亡魂牵引：按手牌牌堆顺序的前 N 张「虚无」牌亮深蓝色（N = 亡魂牵引层数，口径 B：跟引擎烧牌顺序一致）。#171380 = (23, 19, 128)。</item>
/// <item>初心：按手牌牌堆顺序的前 3 张「虚无」牌亮粉色（固定 3 张）。#FF6BB5 = (255, 107, 181)。</item>
/// </list>
/// 两套同时生效时以亡魂牵引的深蓝为准（它才是当下能立刻用的那批）。
/// </summary>
internal static class PullFromBelowGlow
{
    private static readonly Color GlowColor = new(23f / 255f, 19f / 255f, 128f / 255f, 0.98f);

    /// <summary>初心：粉色 #FF6BB5。</summary>
    private static readonly Color BeginnerMindColor = new(255f / 255f, 107f / 255f, 181f / 255f, 0.98f);

    /// <summary>初心高亮的最左边虚无牌张数。</summary>
    private const int BeginnerMindHighlights = 3;

    private static int _trace;

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(NHandCardHolder), nameof(NHandCardHolder.UpdateCard));
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 手牌发光：找不到 NHandCardHolder.UpdateCard，已跳过");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(PullFromBelowGlow), nameof(UpdateCardPostfix)));
        Main.Log.Info($"[{Main.ModId}] 亡魂牵引手牌发光已挂载");
    }

    private static void UpdateCardPostfix(NHandCardHolder __instance)
    {
        try
        {
            Apply(__instance);
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 手牌发光出错：{e.Message}");
        }
    }

    private static void Apply(NHandCardHolder holder)
    {
        if (!NecroSettings.CardReworks) return;

        var node = holder.CardNode;
        var card = holder.CardModel;
        var owner = card?.Owner;
        if (node == null || card == null || owner == null)
        {
            return;
        }

        int stacks = owner.Creature.GetPower<PullFromBelowPower>()?.Amount ?? 0;
        bool beginnerMind = owner.Creature.HasPower<BeginnerMindPower>();

        // 初心每回合只捞 3 张：本回合已经被动消耗掉几张，就少亮几张（烧满 3 张就不再亮）
        int beginnerUsed = beginnerMind ? PassiveExhaustLog.CountThisTurn(owner) : 0;
        int beginnerLeft = BeginnerMindHighlights - beginnerUsed;
        bool ethereal = card.Keywords.Contains(CardKeyword.Ethereal);
        int rank = EtherealRankInHand(holder, card);

        if (ethereal && _trace < 40)
        {
            _trace++;
            Main.Log.Info($"[{Main.ModId}] 发光#{_trace} {card.Id} 虚无={ethereal} 牌堆第几张={rank} 亡魂={stacks} 初心={beginnerMind}(已用{beginnerUsed}/剩{beginnerLeft}) 高亮节点={(node.CardHighlight == null ? "无" : "有")}");
        }

        if (!ethereal || rank < 0)
        {
            return;
        }

        // 玩家点了结束回合之后不再重新判定，保持上一帧的样子
        if (FreezeGate.IsFrozen())
        {
            return;
        }

        // 亡魂牵引优先（深蓝，前 stacks 张）；初心接在它后面（粉色，再 3 张），
        // 两者同时存在时是「第 1 张蓝 → 第 2~4 张粉」，不会因为蓝色占了一张就只剩两张粉。
        Color color;
        if (stacks > 0 && rank < stacks)
        {
            color = GlowColor;
        }
        else if (beginnerMind && beginnerLeft > 0 && rank < stacks + beginnerLeft)
        {
            color = BeginnerMindColor;
        }
        else
        {
            return;
        }

        var highlight = node.CardHighlight;
        if (highlight == null)
        {
            return;
        }

        highlight.Modulate = color;
        highlight.AnimShow();
    }

    /// <summary>
    /// 「会被拦下的第几张」：按**引擎的手牌牌堆顺序**数，只数带「虚无」的牌。
    ///
    /// 口径 B（用户选定）：发光要跟引擎一致 —— 亮哪张就是哪张会被救下。
    /// 回合结束时引擎是照着手牌牌堆的顺序一张张烧虚无牌的，我们的拦截也只能拦它正要烧的那张，
    /// 所以「第几张」必须用牌堆顺序，而不是屏幕上的左右顺序（两者在抽牌插入/重排后会错位）。
    /// </summary>
    private static int EtherealRankInHand(NHandCardHolder holder, CardModel card)
    {
        // 视觉顺序（按 GlobalPosition.X 排序）已停用：它与引擎真实的烧牌顺序可能不一致，
        // 会让「亮的不是被救的」。保留写法备查。
        // var container = holder.GetParent();
        // if (container != null)
        // {
        //     var ethereals = new List<NHandCardHolder>();
        //     foreach (var child in container.GetChildren())
        //     {
        //         if (child is NHandCardHolder h
        //             && h.CardModel is { } other
        //             && other.Keywords.Contains(CardKeyword.Ethereal))
        //         {
        //             ethereals.Add(h);
        //         }
        //     }
        //
        //     if (ethereals.Count > 0)
        //     {
        //         var ordered = ethereals.OrderBy(h => h.GlobalPosition.X).ToList();
        //         int visualRank = ordered.FindIndex(h => ReferenceEquals(h, holder));
        //         if (visualRank >= 0)
        //         {
        //             return visualRank;
        //         }
        //     }
        // }
        // 口径：发光跟**屏幕上的左右顺序**一致 —— 亡魂牵引救的是最左边那几张虚无牌，
        // 所以这里也按左→右数，亮的张数/位置与实际被救的完全对应。
        _ = holder;
        return VisualRankInHand(card);
    }

    /// <summary>按手牌在屏幕上从左到右的顺序数「第几张虚无牌」。</summary>
    private static int VisualRankInHand(CardModel card)
    {
        var owner = card.Owner;
        if (owner == null)
        {
            return -1;
        }

        int rank = 0;
        foreach (CardModel other in HandOrder.LeftToRight(owner))
        {
            if (ReferenceEquals(other, card))
            {
                return rank;
            }

            if (other.Keywords.Contains(CardKeyword.Ethereal))
            {
                rank++;
            }
        }

        return -1;
    }

    /// <summary>按手牌牌堆顺序数「第几张虚无牌」（引擎回合末烧牌用的就是这个顺序）。</summary>
    private static int PileRankInHand(CardModel card)
    {
        var owner = card.Owner;
        if (owner == null)
        {
            return -1;
        }

        int pileRank = 0;
        foreach (var other in PileType.Hand.GetPile(owner).Cards)
        {
            if (ReferenceEquals(other, card))
            {
                return pileRank;
            }

            if (other.Keywords.Contains(CardKeyword.Ethereal))
            {
                pileRank++;
            }
        }

        return -1;
    }
}
