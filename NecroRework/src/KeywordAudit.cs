using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 关键词自检（排查「灵魂之力」无法选中某张消耗牌时用）：
/// 在模型库初始化完成后打一份日志，列出关键卡牌当前的 <c>CanonicalKeywords</c> 与
/// 「灵魂之力」是否认为它们可以被附魔。
/// </summary>
internal static class KeywordAudit
{
    private static bool _done;

    public static void Install(Harmony harmony)
    {
        var init = AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Init));
        if (init is null)
        {
            Main.Log.Warn($"[{Main.ModId}] 关键词自检：找不到 ModelDb.Init，跳过");
            return;
        }

        harmony.Patch(init, postfix: new HarmonyMethod(AccessTools.Method(typeof(KeywordAudit), nameof(InitPostfix))));
    }

    private static void InitPostfix()
    {
        if (_done) return;
        _done = true;

        try
        {
            var souls = ModelDb.Enchantment<SoulsPower>();
            var watch = new List<CardModel> { ModelDb.Card<Dirge>(), ModelDb.Card<Retrieve>(), ModelDb.Card<Exchange>() };

            foreach (CardModel card in watch)
            {
                string keywords = string.Join(",", card.CanonicalKeywords);
                Main.Log.Info($"[{Main.ModId}] 关键词自检：{card.Id} 本地={keywords}（灵魂之力可附魔={souls.CanEnchant(card)}）");
            }

            // 顺带列出所有「消耗」牌，方便对比
            string exhaustCards = string.Join(",", ModelDb.AllCards
                .Where(c => c.CanonicalKeywords.Contains(CardKeyword.Exhaust))
                .Select(c => c.Id.Entry)
                .OrderBy(id => id));
            Main.Log.Info($"[{Main.ModId}] 关键词自检：带「消耗」的牌共 {exhaustCards.Split(',').Length} 张 → {exhaustCards}");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 关键词自检失败：{e}");
        }
    }
}
