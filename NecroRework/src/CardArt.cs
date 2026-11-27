using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NecroRework.Cards;

using NecroRework.Settings;
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
        [typeof(Mend)] = "mend.png",
        [typeof(FleetingMoment)] = "fleeting_moment.png",
        [typeof(FleetingLife)] = "fleeting_life.png",
        [typeof(RiverOfOblivion)] = "river_of_oblivion.png",
        [typeof(SoulBarrier)] = "soul_barrier.png",
        [typeof(Continuation)] = "continuation.png",
        [typeof(GhostLantern)] = "ghost_lantern.png",
        [typeof(Overdraft)] = "overdraft.png",
        [typeof(LivingEnd)] = "living_end.png",
        [typeof(BeginnerMind)] = "beginner_mind.png",
        [typeof(Sublimation)] = "sublimation.png",
        [typeof(SoulGather)] = "soul_gather.png",
        [typeof(OminousPunch)] = "ominous_punch.png",
        [typeof(AllIsVoid)] = "all_is_void.png",
        [typeof(WorldBurden)] = "world_burden.png",
        [typeof(GraveFriend)] = "grave_friend.png",
        [typeof(LifeAndDeath)] = "life_and_death.png",
        [typeof(Drag)] = "drag.png",
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
                harmony.Patch(getter, prefix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(PortraitPrefix)))
        {
            priority = Priority.First,
        });
            }

            // 卡牌大全的大图走 Model.BigPortrait（内部走 packed/card_portraits 的 png，
            // 我们的卡没有那个 png，会回落到 Portrait）。这里直接把它也接管，双保险。
            var bigGetter = AccessTools.PropertyGetter(typeof(CardModel), "BigPortrait");
            if (bigGetter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.BigPortrait");
            }
            else
            {
                harmony.Patch(bigGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(PortraitPrefix)))
                {
                    priority = Priority.First,
                });
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

            // 第四条路：NCard.Reload 之后（0.114 起放大预览会走 Reload，它会把 _portrait.Material
            // 换成模糊材质、并把贴图重新设成 Model.Portrait，我们盖的那层就被冲掉了）。
            var reload = AccessTools.Method(typeof(NCard), "Reload");
            if (reload == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.Reload，刷新层卡图兜底未挂");
            }
            else
            {
                harmony.Patch(reload, postfix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(UpdateVisualsPostfix))));
            }

            // 第五条路：NInspectCardScreen（卡牌大全「点进去」的大图）直接调
            // NCard.SetPortraitTexture(Model.BigPortrait)，既不经过 Reload 也不经过 UpdateVisuals，
            // 所以前面四层兜底全都轮不到；而它沿用放大态那层模糊材质，UV 还是图集 UV → 采到 BETA。
            var setTexture = AccessTools.Method(typeof(NCard), "SetPortraitTexture");
            if (setTexture == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.SetPortraitTexture，大图兜底未挂");
            }
            else
            {
                harmony.Patch(setTexture, postfix: new HarmonyMethod(AccessTools.Method(typeof(CardArt), nameof(SetPortraitTexturePostfix))));
            }

            Main.Log.Info($"[{Main.ModId}] 卡图替换已挂载（模型层 {getter != null} / 节点层 {update != null} / 收尾层 {visuals != null} / 刷新层 {reload != null} / 大图层 {setTexture != null}）");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图替换挂载失败: {e}");
        }
    }

    /// <summary>返回 false 表示我们自己给了贴图；true 表示交回原版逻辑。</summary>
    private static bool PortraitPrefix(CardModel __instance, ref Texture2D __result)
    {
        if (!NecroSettings.CardArt) return true;

        try
        {
            Texture2D texture = Resolve(__instance, out bool handled);
            if (!handled) return true;

            // 只要是我们认领的卡，就绝不能让原版逻辑去加载图集路径——
            // 那条路径在我们的卡上必然 404，游戏会回退成 beta 占位图（放大后尤其明显）。
            __result = texture ?? Placeholder();
            return false;
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

    /// <summary>
    /// 卡牌大全「点进去」的大图走 <c>NCard.SetPortraitTexture(Model.BigPortrait)</c>，
    /// 不经过 Reload / UpdateVisuals；这里补上 UV 修正与模糊材质清理。
    /// </summary>
    private static void SetPortraitTexturePostfix(NCard __instance, Texture2D texture)
    {
        if (!NecroSettings.CardArt) return;

        try
        {
            if (!GodotObject.IsInstanceValid(__instance)) return;
            if (!__instance.IsNodeReady() || PortraitRef(__instance) == null) return;

            // 只处理我们自己的贴图（含占位图），别去动原版卡
            CardModel? model = __instance.Model;
            if (model == null) return;
            Resolve(model, out bool handled);
            if (!handled) return;

            FixShaderUv(__instance);
            ClearBlurMaterial(__instance);
            // if (texture != null) Watch(__instance, texture);  ← 同上，停用
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 大图卡图修正失败: {e}");
        }
    }

    /// <summary>整张卡刷新完之后再补一次（放大/预览时最靠后的兜底）。</summary>
    private static void UpdateVisualsPostfix(NCard __instance)
    {
        Apply(__instance);
    }

    private static void Apply(NCard node)
    {
        if (!NecroSettings.CardArt) return;

        try
        {
            CardModel model = node?.Model;
            if (model == null) return;

            if (!GodotObject.IsInstanceValid(node)) return;

            // 节点还没 _Ready 时（NCard.Create → set_Model → Reload 这条链）内部字段
            // _portrait / _ancientPortrait 都还是 null，直接调 SetPortraitTexture 会抛 NRE。
            // 这种情况下不能只是「放弃」——否则这张牌就永远是原版/占位图（放大后尤其明显），
            // 所以挂到 Ready 信号上，等节点真正就绪后再补一次。
            if (!node.IsNodeReady() || PortraitRef(node) == null)
            {
                Defer(node);
                return;
            }

            Texture2D texture = Resolve(model, out bool handled);
            if (!handled || texture == null)
            {
                // 这张不是我们的卡：但节点可能是从我们自己的卡上回收复用的，
                // 上一次盖贴图时把 uv_region 设成了「整张图」，留着不放会把原版图集卡面
                // 画成图集的一角（看起来就像卡面被削掉一半）。这里用引擎自己的
                // UpdatePortraitBlurParams 重新按贴图算一次 UV，原版卡与我们的卡都安全。
                ResetUv(node);
                return;
            }

            node.SetPortraitTexture(texture);
            FixShaderUv(node);
            ClearBlurMaterial(node);
            // Watch(node, texture);  ← 已停用：节点会被回收复用，定时器会把旧贴图盖到别的卡上
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

    /// <summary>已经挂过 Ready 回调的节点（按实例 ID 记，避免重复挂）。</summary>
    private static readonly HashSet<ulong> Deferred = new();

    /// <summary>节点还没就绪：挂一次性 Ready 回调，就绪后重新盖一次卡图。</summary>
    private static void Defer(NCard node)
    {
        ulong id = node.GetInstanceId();
        if (!Deferred.Add(id)) return;

        node.Ready += () =>
        {
            Deferred.Remove(id);
            if (GodotObject.IsInstanceValid(node)) Apply(node);
        };

        node.TreeExiting += () => Deferred.Remove(id);
    }

    // private static readonly Dictionary<ulong, int> WatchCount = new();
    // 
    // private const int MaxWatchChecks = 12;
    // 
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
    
    /// <summary>引擎自带的 UV 重算（NCard.UpdatePortraitBlurParams）：按当前贴图重新算 uv_region。</summary>
    private static readonly System.Reflection.MethodInfo? ResetUvMethod =
        AccessTools.Method(typeof(NCard), "UpdatePortraitBlurParams");

    private static void ResetUv(NCard node)
    {
        try
        {
            if (ResetUvMethod == null) return;
            if (!GodotObject.IsInstanceValid(node)) return;
            ResetUvMethod.Invoke(node, null);
        }
        catch (Exception e)
        {
            Main.Log.Warn($"[{Main.ModId}] 原版卡面 UV 复原失败: {e.Message}");
        }
    }

    private static void SetFullUv(TextureRect? rect)
    {
        if (rect?.Texture == null) return;
        rect.SetInstanceShaderParameter("uv_region", new Rect2(0f, 0f, 1f, 1f));
    }
    
    /// <summary>
    /// 放大/牌堆预览时引擎会给卡面套一层「模糊材质」（<c>card_portrait_blur_material</c>），
    /// 该材质靠图集 UV 采样；我们的 png 不在图集里，采样会落到图集左上角的 BETA 占位图。
    /// 0.114 里 NCard.Reload 会重新挂这个材质，所以每次盖完贴图都把它摘掉：
    /// 我们自己画的图本来就不需要模糊，直接显示反而更清楚。
    /// </summary>
    private static void ClearBlurMaterial(NCard node)
    {
        try
        {
            TextureRect? portrait = PortraitRef(node);
            if (portrait != null) portrait.Material = null;
            TextureRect? ancient = AncientPortraitRef(node);
            if (ancient != null) ancient.Material = null;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 卡图模糊材质清理失败: {e}");
        }
    }
    // 
    // /// <summary>
    // /// 放大预览时还会有别的节点代码在我们之后把贴图改回 <c>Model.Portrait</c> 的旧值，
    // /// 这里隔几帧再确认一次（最多 6 次，避免一直跑）。
    // /// </summary>
    // private static void Watch(NCard node, Texture2D texture)
    // {
    //     ulong id = node.GetInstanceId();
    //     if (WatchCount.TryGetValue(id, out int count) && count >= MaxWatchChecks) return;
    // 
    //     SceneTree? tree = node.GetTree();
    //     if (tree == null) return;
    // 
    //     WatchCount[id] = (WatchCount.TryGetValue(id, out int c) ? c : 0) + 1;
    // 
    //     SceneTreeTimer timer = tree.CreateTimer(0.08);
    //     timer.Timeout += () =>
    //     {
    //         try
    //         {
    //             if (!GodotObject.IsInstanceValid(node)) return;
    //             TextureRect? rect = PortraitRef(node);
    //             if (rect != null && !ReferenceEquals(rect.Texture, texture))
    //             {
    //                 node.SetPortraitTexture(texture);
    //                 FixShaderUv(node);
    //                 Watch(node, texture);
    //             }
    //         }
    //         catch (Exception e)
    //         {
    //             Main.Log.Error($"[{Main.ModId}] 卡图二次补盖失败: {e}");
    //         }
    //     };
    // }
    // 
    // 
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
            return Placeholder();
        }

        return null;
    }

    /// <summary>我们的卡读不到图时的兜底贴图（游戏自带的 beta 图）。</summary>
    private static Texture2D Placeholder() =>
        _placeholder ??= ResourceLoader.Load<Texture2D>(CardModel.MissingPortraitPath, null, ResourceLoader.CacheMode.Reuse);

    /// <summary>还没有专属卡图的新卡，统一用游戏自带的 beta 占位图。</summary>
    private static bool UsesPlaceholder(CardModel card) =>
        card is Cover or Gaze
            or Forsake or TurnToDust or RebornAnew or Wavering;

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
