using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Settings;

namespace NecroRework;

/// <summary>
/// 把 localization/{语言}/{表名}.json 里的文本写进游戏对应的本地化表。
/// 文件名 = 表名：cards.json → cards 表，powers.json → powers 表，enchantments.json → enchantments 表。
/// key 用游戏的原始格式，例如 BLIGHT_STRIKE.title、DIE_FOR_YOU_POWER.description。
///
/// 另外挂一手 <see cref="LocTable.GetRawText"/> 的前缀：别的 mod（例如 Guida 卡牌拓展）
/// 也可能定义同名 key，谁最后合并谁生效，而合并时机不受我们控制——于是会出现
/// 「卡面文案是别人的、变量是我们自己的」，格式化直接失败并原样显示 {Damage:diff()}。
/// 所以只要「最高优先级」开着，我们自己的 key 一律用我们文件里的文本。
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
internal static class LocOverrides
{
    /// <summary>key → 我们自己的文本（当前语言；缺 current 语言时用 zhs）。</summary>
    private static readonly Dictionary<string, string> Owned = new(StringComparer.Ordinal);

    private static readonly FieldInfo Translations =
        AccessTools.Field(typeof(LocTable), "_translations");

    private static readonly FieldInfo TableName =
        AccessTools.Field(typeof(LocTable), "_name");

    private static bool _preferredLoaded;

    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            Apply();
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 本地化注入失败: {e}");
        }

        try
        {
            InstallRawTextGuard();
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 本地化优先级补丁失败: {e}");
        }
    }

    private static void Apply()
    {
        foreach (var lang in new[] { "zhs", "eng" })
        {
            var dir = Path.Combine(Main.ModDirectory, "localization", lang);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var tableName = Path.GetFileNameWithoutExtension(file);
                var map = ReadMap(file);
                if (map is not { Count: > 0 }) continue;

                var table = LocManager.Instance.GetTable(tableName);
                if (table == null)
                {
                    Main.Log.Warn($"[{Main.ModId}] 找不到本地化表 {tableName}");
                    continue;
                }

                if (Translations?.GetValue(table) is not Dictionary<string, string> dict)
                {
                    Main.Log.Error($"[{Main.ModId}] 拿不到 {tableName} 表的 _translations 字段");
                    continue;
                }

                foreach (var kv in map)
                    dict[kv.Key] = kv.Value;

                Main.Log.Info($"[{Main.ModId}] 写入 {lang}/{tableName} 文本 {map.Count} 条");
            }
        }
    }

    private static Dictionary<string, string>? ReadMap(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 解析 {file} 失败: {e.Message}");
            return null;
        }
    }

    /// <summary>把当前语言的文本装进 Owned（惰性，一次性）。</summary>
    private static void EnsurePreferred()
    {
        if (_preferredLoaded) return;
        _preferredLoaded = true;

        string lang = LocManager.Instance?.Language ?? "zhs";
        var langs = new List<string> { lang };
        if (lang != "zhs") langs.Add("zhs");

        foreach (var candidate in langs)
        {
            var dir = Path.Combine(Main.ModDirectory, "localization", candidate);
            if (!Directory.Exists(dir)) continue;

            int count = 0;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var map = ReadMap(file);
                if (map == null) continue;
                foreach (var kv in map)
                {
                    Owned[kv.Key] = kv.Value;
                    count++;
                }
            }

            if (count > 0)
            {
                Main.Log.Info($"[{Main.ModId}] 本地化优先表就绪（{candidate}，{count} 条）");
                return;
            }
        }
    }

    private static void InstallRawTextGuard()
    {
        var harmony = new Harmony($"{Main.ModId}.loc");
        var method = AccessTools.Method(typeof(LocTable), nameof(LocTable.GetRawText), [typeof(string)]);
        if (method == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 LocTable.GetRawText，跳过本地化优先补丁");
            return;
        }

        var prefix = new HarmonyMethod(typeof(LocOverrides), nameof(GetRawTextPrefix));
        // 后置补丁挂两份（最高/最低优先级各一），无论 Harmony 对 postfix 的排序方向如何，
        // 最后一个执行的一定是我们，值相同所以重复执行也无害——用来压过别的 mod 的 GetRawText postfix。
        var postfixTop = new HarmonyMethod(typeof(LocOverrides), nameof(GetRawTextPostfix)) { priority = 10000 };
        var postfixBottom = new HarmonyMethod(typeof(LocOverrides), nameof(GetRawTextPostfix)) { priority = -10000 };

        if (NecroSettings.ForceOverride)
        {
            prefix.priority = Priority.First;
            harmony.Patch(method, prefix, postfixTop);
            harmony.Patch(method, postfix: postfixBottom);
        }
        else
        {
            harmony.Patch(method, prefix);
        }

        Main.Log.Info($"[{Main.ModId}] 本地化优先补丁已挂载（priority={(NecroSettings.ForceOverride ? "First + 双向后置" : "Normal")}）");
    }

    /// <summary>我们自己的 key 一律返回我们的文本，避免被别的 mod 的本地化表盖掉。</summary>
    private static bool GetRawTextPrefix(LocTable __instance, string key, ref string __result)
    {
        if (!NecroSettings.CardReworks || !NecroSettings.ForceOverride) return true;
        if (string.IsNullOrEmpty(key)) return true;

        EnsurePreferred();
        if (Owned.Count == 0) return true;
        if (!Owned.TryGetValue(key, out var text)) return true;

        // 只接管卡牌 / 能力 / 附魔 / 遗物这四张表，别的表不碰。
        var tableName = TableName?.GetValue(__instance) as string;
        if (tableName is not ("cards" or "powers" or "enchantments" or "relics")) return true;

        __result = text;
        return false;
    }

    /// <summary>
    /// 兜住「别人用 postfix 覆盖了我们 prefix 的结果」这种情况（Guida 的卡牌拓展就是这么干的）。
    /// 两份不同优先级的同一个方法，保证其中一份最后执行。
    /// </summary>
    private static void GetRawTextPostfix(LocTable __instance, string key, ref string __result)
    {
        if (!NecroSettings.CardReworks || !NecroSettings.ForceOverride) return;
        if (string.IsNullOrEmpty(key)) return;

        EnsurePreferred();
        if (Owned.Count == 0) return;
        if (!Owned.TryGetValue(key, out var text)) return;

        var tableName = TableName?.GetValue(__instance) as string;
        if (tableName is not ("cards" or "powers" or "enchantments" or "relics")) return;

        __result = text;
    }
}
