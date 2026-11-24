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
        [typeof(WorldBurden)] = "world_burden.png",
        [typeof(GraveFriend)] = "grave_friend.png",
        [typeof(LifeAndDeath)] = "life_and_death.png",
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

            // 第三条路：UpdateVisuals 整段收尾之后再盖一次。
            // 放大/预览时 UpdateVisuals 会在 UpdatePortrait 之后继续跑 ReloadOverlay 之类的东西，
            // 有可能把贴图重置回原版图集（我们的 png 不在图集里 → 直接空白）。
            var visuals = AccessTools.Method(typeof(NCard), "UpdateVisuals");
            if (visuals == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.UpdateVisuals，放大预览兜底未挂");
            }
            else
            {
                harmony.Patch(visuals, postfix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(UpdateVisualsPostfix))));
            }

            Main.Log.Info($"[{Main.ModId}] 卡图替换已挂载（模型层 {getter != null} / 节点层 {update != null} / 收尾层 {visuals != null}）");
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
        Apply(__instance);
    }

    /// <summary>整张卡刷新完之后再补一次（放大/预览时最靠后的兜底）。</summary>
    private static void UpdateVisualsPostfix(NCard __instance)
    {
        Apply(__instance);
    }

    private static void Apply(NCard node)
    {
        try
        {
            CardModel model = node?.Model;
            if (model == null) return;

            Texture2D texture = Resolve(model, out bool handled);
            if (handled && texture != null)
            {
                node.SetPortraitTexture(texture);
                FixShaderUv(node);
                Watch(node, texture);
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图覆盖失败: {e}");
        }
    }

    private static readonly AccessTools.FieldRef<NCard, TextureRect> PortraitRef =
        AccessTools.FieldRefAccess<NCard, TextureRect>("_portrait");

    private static readonly AccessTools.FieldRef<NCard, TextureRect> AncientPortraitRef =
        AccessTools.FieldRefAccess<NCard, TextureRect>("_ancientPortrait");

    private static readonly Dictionary<ulong, int> WatchCount = new();

    private const int MaxWatchChecks = 6;

    /// <summary>
    /// 我们自己载进来的贴图不在图集里，<c>UpdatePortraitBlurParams</c> 会用
    /// <c>ImageHelper.GetAtlasUvRegion(贴图)</c> 算出一块无效的 UV 区域，
    /// 放大预览（带模糊材质）时就会采到图集左上角的 BETA 占位图 —— 表现为「放大后卡面变 BETA」。
    /// 所以盖完贴图要顺手把 uv_region 设成整张图。
    /// </summary>
    private static void FixShaderUv(NCard node)
    {
        try
        {
            SetFullUv(PortraitRef(node));
            SetFullUv(AncientPortraitRef(node));
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图 UV 修正失败: {e}");
        }
    }

    private static void SetFullUv(TextureRect? rect)
    {
        if (rect?.Texture == null) return;
        rect.SetInstanceShaderParameter("uv_region", new Rect2(0f, 0f, 1f, 1f));
    }

    /// <summary>
    /// 放大预览时还会有别的节点代码在我们之后把贴图改回 <c>Model.Portrait</c> 的旧值，
    /// 这里隔几帧再确认一次（最多 6 次，避免一直跑）。
    /// </summary>
    private static void Watch(NCard node, Texture2D texture)
    {
        ulong id = node.GetInstanceId();
        if (WatchCount.TryGetValue(id, out int count) && count >= MaxWatchChecks) return;

        SceneTree? tree = node.GetTree();
        if (tree == null) return;

        WatchCount[id] = (WatchCount.TryGetValue(id, out int c) ? c : 0) + 1;

        SceneTreeTimer timer = tree.CreateTimer(0.08);
        timer.Timeout += () =>
        {
            try
            {
                if (!GodotObject.IsInstanceValid(node)) return;
                TextureRect? rect = PortraitRef(node);
                if (rect != null && !ReferenceEquals(rect.Texture, texture))
                {
                    node.SetPortraitTexture(texture);
                    FixShaderUv(node);
                    Watch(node, texture);
                }
            }
            catch (Exception e)
            {
                Main.Log.Error($"[{Main.ModId}] 卡图二次补盖失败: {e}");
            }
        };
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
        card is FleetingLife or Continuation or Drag or Cover or Sublimation or BeginnerMind or Mend or Gaze or SoulGather or Forsake or SoulBarrier or RiverOfOblivion or TurnToDust or RebornAnew;

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
