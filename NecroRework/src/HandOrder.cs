using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace NecroRework;

/// <summary>
/// 手牌的「视觉顺序」工具。
/// 引擎的 <c>CardPile.Cards</c> 是插入顺序，打出一张牌之后列表顺序和玩家看到的
/// 左→右排布可能对不上（节点会保留原来的槽位），所以按「节点横坐标」判定左右更可靠。
/// </summary>
internal static class HandOrder
{
    /// <summary>取手牌里最右边那张（按节点横坐标），取不到返回 null。</summary>
    public static CardModel? RightmostCard(Player player)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            {
                return null;
            }

            IReadOnlyList<CardModel> hand = PileType.Hand.GetPile(player).Cards;

            CardModel? best = null;
            float bestX = float.MinValue;

            foreach (Node node in tree.Root.FindChildren("*", "", true, false))
            {
                if (node is not NHandCardHolder holder)
                {
                    continue;
                }

                if (holder.CardModel is not { } card)
                {
                    continue;
                }

                if (card.Owner != player)
                {
                    continue;
                }

                bool inHand = false;
                foreach (CardModel member in hand)
                {
                    if (ReferenceEquals(member, card))
                    {
                        inHand = true;
                        break;
                    }
                }

                if (!inHand)
                {
                    continue;
                }

                float x = holder.GlobalPosition.X;
                if (x > bestX)
                {
                    bestX = x;
                    best = card;
                }
            }

            return best;
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 手牌顺序判定失败，回退到列表顺序：{e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 取整副手牌按「屏幕上从左到右」排好的顺序。
    /// 找不到节点的牌（刚抽到还没摆位）按牌堆顺序补在末尾，保证不会漏牌。
    /// </summary>
    public static List<CardModel> LeftToRight(Player player)
    {
        var hand = PileType.Hand.GetPile(player).Cards;
        var result = new List<CardModel>(hand.Count);

        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            {
                return new List<CardModel>(hand);
            }

            var withX = new List<(CardModel Card, float X)>();
            foreach (Node node in tree.Root.FindChildren("*", "", true, false))
            {
                if (node is not NHandCardHolder holder)
                {
                    continue;
                }

                if (holder.CardModel is not { } card || card.Owner != player)
                {
                    continue;
                }

                if (!Contains(hand, card))
                {
                    continue;
                }

                withX.Add((card, holder.GlobalPosition.X));
            }

            withX.Sort((a, b) => a.X.CompareTo(b.X));
            foreach (var pair in withX)
            {
                if (!Contains(result, pair.Card))
                {
                    result.Add(pair.Card);
                }
            }

            // 没摆位的牌按牌堆顺序补在后面
            foreach (CardModel card in hand)
            {
                if (!Contains(result, card))
                {
                    result.Add(card);
                }
            }
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 手牌左右排序失败，回退到列表顺序：{e.Message}");
            return new List<CardModel>(hand);
        }

        return result;
    }

    private static bool Contains(IReadOnlyList<CardModel> list, CardModel card)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], card))
            {
                return true;
            }
        }

        return false;
    }
}
