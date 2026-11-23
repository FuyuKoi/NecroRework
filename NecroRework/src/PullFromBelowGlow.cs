using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 手牌提示（两套）：
/// <list type="bullet">
/// <item>亡魂牵引：从左到右的前 N 张「虚无」牌亮深蓝色（N = 亡魂牵引层数）。#171380 = (23, 19, 128)。</item>
/// <item>初心：从左到右的前 3 张「虚无」牌亮粉色（固定 3 张）。#FF6BB5 = (255, 107, 181)。</item>
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
        var node = holder.CardNode;
        var card = holder.CardModel;
        var owner = card?.Owner;
        if (node == null || card == null || owner == null)
        {
            return;
        }

        int stacks = owner.Creature.GetPower<PullFromBelowPower>()?.Amount ?? 0;
        bool beginnerMind = owner.Creature.HasPower<BeginnerMindPower>();
        bool ethereal = card.Keywords.Contains(CardKeyword.Ethereal);
        int rank = EtherealRankInHand(holder, card);

        if (ethereal && _trace < 40)
        {
            _trace++;
            Main.Log.Info($"[{Main.ModId}] 发光#{_trace} {card.Id} 虚无={ethereal} 第几张虚无={rank} 亡魂={stacks} 初心={beginnerMind} 高亮节点={(node.CardHighlight == null ? "无" : "有")}");
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

        // 亡魂牵引优先（深蓝）；没有的话再看初心（粉色，固定最左 3 张虚无牌）
        Color color;
        if (stacks > 0 && rank < stacks)
        {
            color = GlowColor;
        }
        else if (beginnerMind && rank < BeginnerMindHighlights)
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
    /// 「会被拦下的第几张」：手牌从左到右数，只数带「虚无」的牌，自己前面有几张。
    /// 结算时也是按手牌顺序一张张烧，所以前 N 张虚无牌正好对应 N 层。
    /// </summary>
    private static int EtherealRankInHand(NHandCardHolder holder, CardModel card)
    {
        var container = holder.GetParent();
        if (container != null)
        {
            int rank = 0;
            foreach (var child in container.GetChildren())
            {
                if (child is not NHandCardHolder h)
                {
                    continue;
                }

                if (ReferenceEquals(h, holder))
                {
                    return rank;
                }

                if (h.CardModel is { } other && other.Keywords.Contains(CardKeyword.Ethereal))
                {
                    rank++;
                }
            }
        }

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
