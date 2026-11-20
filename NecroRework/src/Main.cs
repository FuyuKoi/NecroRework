using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Modding;
using NecroRework.Cards;

namespace NecroRework;

/// <summary>
/// 模组入口。游戏加载本 DLL 后会调用带 [ModInitializer] 的静态方法。
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class Main
{
    public const string ModId = "NecroRework";

    public static readonly Logger Log = new(ModId, LogType.Generic);

    /// <summary>mods\NecroRework\ 目录，用来读取本地化 JSON 等外部文件。</summary>
    public static string ModDirectory =>
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

    public static void Initialize()
    {
        try
        {
            var harmony = new Harmony(ModId);

            // 应急开关：mod 目录下有 no_osty_split.flag 时关闭 50% 分摊
            OstySplit.Enabled = !File.Exists(Path.Combine(ModDirectory, "no_osty_split.flag"));
            if (!OstySplit.Enabled)
                Log.Warn($"[{ModId}] 检测到 no_osty_split.flag：奥斯提 50% 分摊已关闭");

            // 1) 特性式补丁（本地化、飘荡、奥斯提 50% 等）
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            // 2) 表驱动补丁（Reworks 里的数值 / 效果改写）
            PatchInstaller.Install(harmony);
            // 3) 新卡牌进卡池（必须在 ModelDb 初始化之前）
            RegisterContent();
            Log.Info($"[{ModId}] patches applied, mod dir = {ModDirectory}");
            Log.Info($"[{ModId}] patched methods = {harmony.GetPatchedMethods().Count()}");
        }
        catch (Exception e)
        {
            Log.Error($"[{ModId}] 初始化失败: {e}");
        }
    }

    /// <summary>
    /// 注册新增内容。卡牌要明确指定进入哪个卡池，否则不会出现在奖励里。
    /// </summary>
    private static void RegisterContent()
    {
        // 调试卡：进亡灵契约师的卡池
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(DriftTestCard));

        // 回眸：普通品质技能牌，跟着白卡一起出现在卡牌奖励里
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(LookBack));

        // 触碰：普通品质攻击牌
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(Touch));

        // 以下五张：普通品质技能牌
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(Retrieve));
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(Exchange));
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(CraftSpirit));
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(Gather));
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(Continuation));

        // 冥灯：罕见品质技能牌（X 费）
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(GhostLantern));

        Log.Info($"[{ModId}] 已注册新卡牌 DriftTestCard / LookBack / Touch / Retrieve / Exchange / CraftSpirit / Gather / Continuation / GhostLantern -> NecrobinderCardPool");
    }
}
