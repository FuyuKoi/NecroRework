using System.IO;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NecroRework.Enchantments;

namespace NecroRework;

/// <summary>
/// 飘荡的附魔图标。
/// 注意：原版 <c>EnchantmentModel.Icon</c> 的类型是 <see cref="CompressedTexture2D"/>，
/// 在现在这版 GodotSharp 里只能由 pck 里导入过的 .ctex 产生
/// （<see cref="PortableCompressedTexture2D"/> 并不继承它，运行期做不出来），
/// 所以这里分两层：
///  1) <c>EnchantmentModel.IconPath</c>（string）前缀 —— 让悬停提示 / 附魔预览这类
///     只认 <c>Icon</c> 的地方拿到一张**原版附魔图标**（否则会显示 missing_enchantment 占位图）；
///  2) <c>NCard.UpdateEnchantmentVisuals</c> 后置 —— 卡面上盖我们自己的 drift.png。
/// </summary>
internal static class DriftIcon
{
    private const string IconFile = "drift.png";

    /// <summary>借用的原版附魔图标（pck 里确实存在），只用于悬停提示。</summary>
    private const string BorrowedIconPath = "enchantments/inky.png";

    private static bool _tried;
    private static Image? _image;
    private static Texture2D? _icon;

    private static readonly AccessTools.FieldRef<NCard, TextureRect> EnchantmentIconRef =
        AccessTools.FieldRefAccess<NCard, TextureRect>("_enchantmentIcon");

    public static void Install(Harmony harmony)
    {
        // 模型层：Icon 只吃 res:// 里的 CompressedTexture2D，改 IconPath 借一张原版图。
        var iconPathGetter = AccessTools.PropertyGetter(typeof(EnchantmentModel), "IconPath");
        if (iconPathGetter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 EnchantmentModel.IconPath，悬停提示会显示占位图标");
        }
        else
        {
            harmony.Patch(iconPathGetter,
                prefix: new HarmonyMethod(typeof(DriftIcon), nameof(IconPathPrefix)));
        }

        var target = AccessTools.Method(typeof(NCard), "UpdateEnchantmentVisuals");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.UpdateEnchantmentVisuals，卡面图标不会替换");
        }
        else
        {
            harmony.Patch(target,
                postfix: new HarmonyMethod(typeof(DriftIcon), nameof(UpdateEnchantmentVisualsPostfix)));
        }

        // 悬停提示：EnchantmentModel.HoverTip 里的 Icon 是 Texture2D（不是 CompressedTexture2D），
        // 所以这里可以直接把自制图标塞进去，鼠标移上去看到的就是我们自己的图。
        var hoverGetter = AccessTools.PropertyGetter(typeof(EnchantmentModel), "HoverTip");
        if (hoverGetter == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 EnchantmentModel.HoverTip，悬停提示图标不会替换");
        }
        else
        {
            harmony.Patch(hoverGetter,
                postfix: new HarmonyMethod(typeof(DriftIcon), nameof(HoverTipPostfix)));
        }

        Main.Log.Info($"[{Main.ModId}] 飘荡图标已挂载（模型层 {iconPathGetter != null} / 节点层 {target != null} / 悬停 {hoverGetter != null}）");
    }

    /// <summary>把悬停提示里的图标换成我们自己的图（只改飘荡）。</summary>
    private static void HoverTipPostfix(EnchantmentModel __instance, ref HoverTip __result)
    {
        try
        {
            if (__instance is not DriftEnchantment) return;

            Texture2D? texture = LoadIcon();
            if (texture == null) return;

            __result = new HoverTip(__instance.Title, __instance.DynamicDescription, texture);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡悬停图标替换失败: {e}");
        }
    }

    /// <summary>返回 false 表示我们自己给了路径。只影响飘荡，别的附魔走原版逻辑。</summary>
    private static bool IconPathPrefix(EnchantmentModel __instance, ref string __result)
    {
        try
        {
            if (__instance is not DriftEnchantment) return true;

            __result = ImageHelper.GetImagePath(BorrowedIconPath);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡图标路径改写失败: {e}");
            return true;
        }
    }

    private static void UpdateEnchantmentVisualsPostfix(NCard __instance)
    {
        try
        {
            if (__instance?.Model?.Enchantment is not DriftEnchantment) return;

            TextureRect? icon = EnchantmentIconRef(__instance);
            if (icon == null) return;

            Texture2D? texture = LoadIcon();
            if (texture != null)
            {
                icon.Texture = texture;
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡图标替换失败: {e}");
        }
    }

    private static Image? LoadImage()
    {
        if (_tried) return _image;
        _tried = true;

        string path = Path.Combine(Main.ModDirectory, IconFile);
        if (!File.Exists(path))
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到附魔图标 {path}");
            return null;
        }

        _image = Image.LoadFromFile(path);
        if (_image == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 附魔图标解不出来 {path}");
        }

        return _image;
    }

    private static Texture2D? LoadIcon()
    {
        if (_icon != null) return _icon;

        Image? image = LoadImage();
        if (image == null) return null;

        _icon = ImageTexture.CreateFromImage(image);
        return _icon;
    }
}
