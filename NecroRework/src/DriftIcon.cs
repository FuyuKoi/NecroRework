using System.IO;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NecroRework.Enchantments;

namespace NecroRework;

/// <summary>
/// 飘荡的附魔图标：原版 <c>EnchantmentModel.Icon</c> 返回的是 <see cref="CompressedTexture2D"/>，
/// 只能从 res:// 里的资源加载，mod 目录里的 png 拿不到那个类型，
/// 所以在卡牌节点刷新附魔图标之后（<c>NCard.UpdateEnchantmentVisuals</c>）把它换成我们自己的贴图。
/// </summary>
internal static class DriftIcon
{
    private const string IconFile = "drift.png";

    private static Texture2D? _icon;
    private static bool _tried;

    private static readonly AccessTools.FieldRef<NCard, TextureRect> EnchantmentIconRef =
        AccessTools.FieldRefAccess<NCard, TextureRect>("_enchantmentIcon");

    public static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(NCard), "UpdateEnchantmentVisuals");
        if (target == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到 NCard.UpdateEnchantmentVisuals，飘荡图标不会替换");
            return;
        }

        harmony.Patch(target,
            postfix: new HarmonyMethod(typeof(DriftIcon), nameof(UpdateEnchantmentVisualsPostfix)));
    }

    private static void UpdateEnchantmentVisualsPostfix(NCard __instance)
    {
        try
        {
            if (__instance?.Model?.Enchantment is not DriftEnchantment) return;

            TextureRect? icon = EnchantmentIconRef(__instance);
            if (icon == null) return;

            Texture2D? texture = Load();
            if (texture != null)
            {
                icon.Texture = texture;
            }
        }
        catch (System.Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡图标替换失败: {e}");
        }
    }

    private static Texture2D? Load()
    {
        if (_tried) return _icon;
        _tried = true;

        string path = Path.Combine(Main.ModDirectory, IconFile);
        if (!File.Exists(path))
        {
            Main.Log.Warn($"[{Main.ModId}] 找不到附魔图标 {path}");
            return null;
        }

        Image? image = Image.LoadFromFile(path);
        if (image == null)
        {
            Main.Log.Warn($"[{Main.ModId}] 附魔图标解不出来 {path}");
            return null;
        }

        _icon = ImageTexture.CreateFromImage(image);
        return _icon;
    }
}
