using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace NecroRework;

/// <summary>
/// 扫荡凝视的「伤害次数」显示：底子是 2 段，玩家身上带「拇指夹」(Thumbscrew) 时变 3 段。
/// 实际命中段数由引擎的 Hook.ModifyAttackHitCount 在 AttackCommand 执行时自己加成，
/// 这里只负责把卡面文字改成和实际一致（2 次 / 3 次）。
/// </summary>
internal static class SweepingGazeHits
{
    private const string HitsKey = "Hits";

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(CardModel), "AddExtraArgsToDescription");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 扫荡凝视段数显示：找不到 CardModel.AddExtraArgsToDescription，跳过");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SweepingGazeHits), nameof(Postfix))));
        Main.Log.Info($"[{Main.ModId}] 扫荡凝视段数显示已挂载（拇指夹会让 2 次变 3 次）");
    }

    private static void Postfix(CardModel __instance, LocString description)
    {
        if (__instance is not SweepingGaze) return;

        description.Add(HitsKey, $"{HitCount(__instance)}次");
    }

    /// <summary>2 段底子；带拇指夹时 +1（与原版遗物 Thumbscrew 的判定条件保持一致）。</summary>
    private static int HitCount(CardModel card)
    {
        if (card.IsCanonical) return 2;

        IEnumerable<RelicModel> relics = card.Owner?.Relics ?? [];
        return relics.Any(relic => relic is Thumbscrew) ? 3 : 2;
    }
}
