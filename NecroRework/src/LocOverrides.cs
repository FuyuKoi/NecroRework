using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework;

/// <summary>
/// 把 localization/{语言}/{表名}.json 里的文本写进游戏对应的本地化表。
/// 文件名 = 表名：cards.json → cards 表，powers.json → powers 表，enchantments.json → enchantments 表。
/// key 用游戏的原始格式，例如 BLIGHT_STRIKE.title、DIE_FOR_YOU_POWER.description。
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
internal static class LocOverrides
{
    private static readonly FieldInfo Translations =
        AccessTools.Field(typeof(LocTable), "_translations");

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
                Dictionary<string, string> map;
                try
                {
                    map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    Main.Log.Error($"[{Main.ModId}] 解析 {file} 失败: {e.Message}");
                    continue;
                }

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
}
