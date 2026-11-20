using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 给自制卡牌换上自己的卡图。
/// 卡面贴图走 CardModel.Portrait（非虚属性，内部是 ResourceLoader.Load(res://...tres)），
/// 所以这里用 Harmony 前缀直接返回我们自己的贴图：
///  - 有专属卡图的卡：从 mod 目录读进来的 PNG；
///  - 还没有卡图的卡：游戏自带的 beta 占位图（CardModel.MissingPortraitPath）。
/// </summary>
internal static class CardArt
{
    private static readonly Dictionary<Type, Texture2D> Cache = new();

    private static readonly Dictionary<Type, string> Files = new()
    {
        [typeof(LookBack)] = "look_back.png",
        [typeof(Touch)] = "touch.png",
        [typeof(Gather)] = "gather.png",
        [typeof(Retrieve)] = "retrieve.png",
        [typeof(Exchange)] = "exchange.png",
        [typeof(CraftSpirit)] = "craft_spirit.png",
        [typeof(Continuation)] = "continuation.png",
        [typeof(GhostLantern)] = "ghost_lantern.png",
    };

    private static Texture2D _placeholder;

    public static void Install(Harmony harmony)
    {
        try
        {
            var getter = AccessTools.PropertyGetter(typeof(CardModel), "Portrait");
            if (getter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.Portrait，跳过卡图替换");
                return;
            }

            harmony.Patch(getter, prefix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(PortraitPrefix))));
            Main.Log.Info($"[{Main.ModId}] 卡图替换已挂载");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图替换挂载失败: {e}");
        }
    }

    /// <summary>返回 false 表示我们自己给了贴图；true 表示交回原版逻辑。</summary>
    private static bool PortraitPrefix(CardModel __instance, ref Texture2D __result)
    {
        try
        {
            Type type = __instance.GetType();
            if (Files.TryGetValue(type, out string fileName))
            {
                if (!Cache.TryGetValue(type, out Texture2D texture))
                {
                    texture = Load(fileName);
                    Cache[type] = texture;
                }

                if (texture != null)
                {
                    __result = texture;
                    return false;
                }
            }

            if (UsesPlaceholder(__instance))
            {
                _placeholder ??= ResourceLoader.Load<Texture2D>(
                    CardModel.MissingPortraitPath, null, ResourceLoader.CacheMode.Reuse);

                if (_placeholder != null)
                {
                    __result = _placeholder;
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图读取失败: {e}");
        }

        return true;
    }

    /// <summary>还没有专属卡图的新卡，统一用游戏自带的 beta 占位图。</summary>
    private static bool UsesPlaceholder(CardModel card) => card is Continuation;

    private static Texture2D Load(string fileName)
    {
        var path = Path.Combine(Main.ModDirectory, fileName);
        if (!File.Exists(path))
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到卡图 {path}，将显示原版占位图");
            return null;
        }

        var image = Image.LoadFromFile(path);
        if (image == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 卡图解不出来 {path}");
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }
}
