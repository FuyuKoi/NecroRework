using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 亡魂牵引手牌提示：手牌从左到右的前 N 张里，带「虚无」的牌会亮起青绿色的高亮描边
/// （N = 亡魂牵引层数）。颜色是不可生效红色 (0.83, 0, 0.33) 的反相，即 #2BFFAB。
/// </summary>
internal static class PullFromBelowGlow
{
    private static readonly Color GlowColor = new(0.17f, 1f, 0.67f, 0.98f);

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

        if (!card.Keywords.Contains(CardKeyword.Ethereal))
        {
            return;
        }

        int stacks = owner.Creature.GetPower<PullFromBelowPower>()?.Amount ?? 0;
        if (stacks <= 0)
        {
            return;
        }

        int index = IndexInHand(card);
        if (index < 0 || index >= stacks)
        {
            return;
        }

        var highlight = node.CardHighlight;
        if (highlight == null)
        {
            return;
        }

        highlight.Modulate = GlowColor;
        highlight.AnimShow();
    }

    private static int IndexInHand(CardModel card)
    {
        var owner = card.Owner;
        if (owner == null)
        {
            return -1;
        }

        var cards = PileType.Hand.GetPile(owner).Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
            {
                return i;
            }
        }

        return -1;
    }
}
