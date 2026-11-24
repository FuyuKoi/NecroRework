using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework;

/// <summary>
/// 让一张牌能同时挂多个「折磨」（恶咒 / 魂缚 / 污染 / 缠身 …）。
/// 原版 <c>CardModel.AfflictInternal</c> 发现 <c>Affliction != null</c> 就直接抛
/// <c>InvalidOperationException: ... already afflicted! ...</c>，
/// 一旦两个来源（比如恶咒的全体附加 + 魂缚污染）想落在同一张牌上，战斗会直接卡死。
///
/// 做法：第一个折磨照旧走原版（拿它自己的 overlay 和战斗钩子），
/// 多出来的记在侧表里 ——
///  · 卡面描述会把它们的额外文字拼上去；
///  · 恶咒这类「加虚无」的效果在这里手动补关键词（侧表里的折磨收不到战斗钩子）；
///  · 移除时按「先来先走」的顺序逐个清掉。
/// </summary>
internal static class MultiAffliction
{
    private static readonly Dictionary<CardModel, List<AfflictionModel>> Extras = new(ReferenceEqualityComparer.Instance);

    private static readonly AccessTools.FieldRef<CardModel, Action?> AfflictionChangedRef =
        AccessTools.FieldRefAccess<CardModel, Action?>("AfflictionChanged");

    private static Action<CardModel, AfflictionModel?>? _setAffliction;

    public static void Install(Harmony harmony)
    {
        MethodInfo? afflict = AccessTools.Method(typeof(CardModel), "AfflictInternal", [typeof(AfflictionModel), typeof(decimal)]);
        MethodInfo? clear = AccessTools.Method(typeof(CardModel), "ClearAfflictionInternal");
        MethodInfo? describe = AccessTools.Method(typeof(CardModel), "GetDescriptionForPile");

        if (afflict == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.AfflictInternal，多折磨共存未挂载");
            return;
        }

        MethodInfo? setter = AccessTools.Property(typeof(CardModel), "Affliction")?.GetSetMethod(true);
        if (setter != null)
        {
            _setAffliction = (card, value) => setter.Invoke(card, [value]);
        }

        harmony.Patch(afflict, prefix: new HarmonyMethod(typeof(MultiAffliction), nameof(AfflictPrefix)));

        if (clear != null)
        {
            harmony.Patch(clear, prefix: new HarmonyMethod(typeof(MultiAffliction), nameof(ClearPrefix)));
        }

        if (describe != null)
        {
            harmony.Patch(describe, postfix: new HarmonyMethod(typeof(MultiAffliction), nameof(DescriptionPostfix)));
        }

        Main.Log.Info($"[{Main.ModId}] 多折磨共存已挂载（附加 {afflict != null} / 移除 {clear != null} / 描述 {describe != null}）");
    }

    private static bool AfflictPrefix(CardModel __instance, AfflictionModel affliction, decimal amount)
    {
        try
        {
            if (__instance?.Affliction == null) return true; // 第一层照旧走原版
            if (affliction == null) return true;

            List<AfflictionModel> list = Extras.TryGetValue(__instance, out List<AfflictionModel>? existing)
                ? existing
                : Extras[__instance] = [];

            // 同一种折磨重复施加：原版也是直接抛，这里直接忽略掉。
            if (list.Any(a => a.GetType() == affliction.GetType()) ||
                __instance.Affliction.GetType() == affliction.GetType())
            {
                return false;
            }

            // 联机安全模式：侧表是「本机私有」的静态表，不参与同步。
            // 与其让两台机器对「谁是主折磨 / 这张牌有没有虚无」各说各话，不如在联机里
            // 直接忽略第二个折磨（不崩、行为确定、双方一致）。
            if (MultiplayerMode.IsMultiplayer(__instance))
            {
                Main.Log.Warn($"[{Main.ModId}] 多折磨共有（联机安全模式）：{__instance.Id} 已有 {__instance.Affliction.Id}，忽略 {affliction.Id}");
                return false;
            }

            affliction.Card = __instance;
            affliction.Amount = (int)amount;
            list.Add(affliction);

            // 恶咒的效果是「这张牌获得虚无」，而侧表里的折磨收不到战斗钩子，这里手动补上。
            if (affliction.Id.Entry.Contains("HEX", StringComparison.OrdinalIgnoreCase))
            {
                __instance.AddKeyword(CardKeyword.Ethereal);
            }

            Raise(__instance);
            Main.Log.Info($"[{Main.ModId}] 多折磨共有：{__instance.Id} 追加 {affliction.Id}（现共 {1 + list.Count} 个）");
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 多折磨附加失败（已忽略这一层，避免战斗卡死）: {e}");
            return false;
        }
    }

    private static bool ClearPrefix(CardModel __instance)
    {
        try
        {
            if (__instance == null) return true;
            if (!Extras.TryGetValue(__instance, out List<AfflictionModel>? list) || list.Count == 0) return true;

            AfflictionModel next = list[0];
            list.RemoveAt(0);
            if (list.Count == 0)
            {
                Extras.Remove(__instance);
            }

            AfflictionModel? current = __instance.Affliction;
            current?.ClearInternal();
            _setAffliction?.Invoke(__instance, next);
            Raise(__instance);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 多折磨移除失败（交回原版）: {e}");
            return true;
        }
    }

    private static void DescriptionPostfix(CardModel __instance, ref string __result)
    {
        try
        {
            if (__instance == null) return;
            if (!Extras.TryGetValue(__instance, out List<AfflictionModel>? list) || list.Count == 0) return;

            var extra = new System.Text.StringBuilder(__result);
            foreach (AfflictionModel affliction in list)
            {
                if (!affliction.HasExtraCardText) continue;
                string? text = affliction.DynamicExtraCardText?.GetFormattedText();
                if (string.IsNullOrWhiteSpace(text)) continue;
                extra.Append("\n").Append(text);
            }

            __result = extra.ToString();
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 多折磨描述拼接失败: {e}");
        }
    }

    private static void Raise(CardModel card)
    {
        try
        {
            AfflictionChangedRef(card)?.Invoke();
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 多折磨刷新失败: {e.Message}");
        }
    }
}
