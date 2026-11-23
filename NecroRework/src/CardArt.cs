using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 给自制卡牌换上自己的卡图。
/// 两条路同时挂：
///  1) CardModel.Portrait 前缀（理想路径，但如果别的 mod 也补了这个 getter，可能轮不到我们）；
///  2) NCard.UpdatePortrait 后置（保险路径：卡牌节点画完之后，直接把贴图盖上去，
///     不管模型那条链上被谁接管都能生效）。
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
        [typeof(Overdraft)] = "overdraft.png",
        [typeof(LivingEnd)] = "living_end.png",
        [typeof(OminousPunch)] = "ominous_punch.png",
        [typeof(AllIsVoid)] = "all_is_void.png",
    };

    private static Texture2D _placeholder;

    private static readonly HashSet<Type> Warned = new();

    public static void Install(Harmony harmony)
    {
        try
        {
            var getter = AccessTools.PropertyGetter(typeof(CardModel), "Portrait");
            if (getter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.Portrait，跳过模型层卡图替换");
            }
            else
            {
                harmony.Patch(getter, prefix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(PortraitPrefix))));
            }

            // 保险路径：卡牌节点自己画完之后再盖一次贴图。
            var update = AccessTools.Method(typeof(NCard), "UpdatePortrait");
            if (update == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.UpdatePortrait，跳过节点层卡图替换");
            }
            else
            {
                harmony.Patch(update, postfix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(UpdatePortraitPostfix))));
            }

            Main.Log.Info($"[{Main.ModId}] 卡图替换已挂载（模型层 {getter != null} / 节点层 {update != null}）");
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
            Texture2D texture = Resolve(__instance, out bool handled);
            if (!handled) return true;

            if (texture != null)
            {
                __result = texture;
                return false;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图读取失败: {e}");
        }

        return true;
    }

    /// <summary>节点画完之后，把我们的贴图盖上去。</summary>
    private static void UpdatePortraitPostfix(NCard __instance)
    {
        try
        {
            CardModel model = __instance.Model;
            if (model == null) return;

            Texture2D texture = Resolve(model, out bool handled);
            if (handled && texture != null)
            {
                __instance.SetPortraitTexture(texture);
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图覆盖失败: {e}");
        }
    }

    /// <summary>
    /// 取这张卡要用的贴图。
    /// handled=false 表示这张卡不归我们管（交回原版）；
    /// handled=true 且 texture 为 null 表示该管但读不到文件（回退原版）。
    /// </summary>
    private static Texture2D Resolve(CardModel card, out bool handled)
    {
        handled = false;
        Type type = card.GetType();

        if (Files.TryGetValue(type, out string fileName))
        {
            handled = true;
            if (Cache.TryGetValue(type, out Texture2D cached)) return cached;

            Texture2D loaded = Load(fileName);
            Cache[type] = loaded;
            Main.Log.Info($"[{Main.ModId}] 卡图 {type.Name} <- {fileName}：{(loaded == null ? "读取失败，回退原版" : "已载入")}");
            return loaded;
        }

        if (UsesPlaceholder(card))
        {
            handled = true;
            _placeholder ??= ResourceLoader.Load<Texture2D>(
                CardModel.MissingPortraitPath, null, ResourceLoader.CacheMode.Reuse);
            return _placeholder;
        }

        return null;
    }

    /// <summary>还没有专属卡图的新卡，统一用游戏自带的 beta 占位图。</summary>
    private static bool UsesPlaceholder(CardModel card) =>
        card is FleetingLife or Continuation or Drag or Cover or Sublimation or LifeAndDeath or BeginnerMind or Mend or Gaze or SoulGather or Forsake or GraveFriend or SoulBarrier or RiverOfOblivion or WorldBurden or TurnToDust or RebornAnew;

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
