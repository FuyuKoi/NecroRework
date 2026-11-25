using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Stats;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 按 Reworks 表里的卡牌类型，动态挂 Harmony 补丁。
/// 用 __args 传参，避免依赖游戏内部参数名（版本更新时更抗性）。
/// </summary>
internal static class PatchInstaller
{
    public static void Install(Harmony harmony)
    {
        // 最高优先级开关：开启时让我们的表驱动改写压过其它 mod 对同一张原版卡的改动。
        // prefix 抢到最高优先级 → 我们先执行，返回 false 后排在我们后面的 prefix 全部被跳过；
        // postfix 用最低优先级 → 我们最后执行，能覆盖别人写进 __result 的值。
        // 只影响我们登记在 Reworks 表里的原版卡类型；别的 mod 新加的卡不在表里，完全不受影响。
        int prefixPriority = NecroSettings.ForceOverride ? 10000 : Priority.Normal;
        int postfixPriority = NecroSettings.ForceOverride ? -10000 : Priority.Normal;

        var varsPostfix = new HarmonyMethod(typeof(Patches), nameof(Patches.VarsPostfix)) { priority = postfixPriority };
        var playPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.PlayPrefix)) { priority = prefixPriority };
        var upgradePostfix = new HarmonyMethod(typeof(Patches), nameof(Patches.UpgradePostfix)) { priority = postfixPriority };
        var upgradePrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.UpgradePrefix)) { priority = prefixPriority };
        var keywordsPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.KeywordsPrefix)) { priority = prefixPriority };
        var tipsPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.TipsPrefix)) { priority = prefixPriority };
        var costPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.CostPrefix)) { priority = prefixPriority };
        var typePostfix = new HarmonyMethod(typeof(Patches), nameof(Patches.TypePostfix)) { priority = postfixPriority };

        foreach (var type in Reworks.Vars.Keys)
        {
            var getter = AccessTools.PropertyGetter(type, "CanonicalVars");
            if (getter == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.CanonicalVars，跳过数值改写");
                continue;
            }
            Safe($"数值改写 {type.Name}", () => harmony.Patch(getter, postfix: varsPostfix));
        }

        foreach (var type in Reworks.Play.Keys)
        {
            var onPlay = AccessTools.Method(type, "OnPlay");
            if (onPlay == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.OnPlay，跳过效果改写");
                continue;
            }
            Safe($"效果改写 {type.Name}", () => harmony.Patch(onPlay, prefix: playPrefix));
        }

        foreach (var type in Reworks.Upgrade.Keys)
        {
            var onUpgrade = AccessTools.Method(type, "OnUpgrade");
            if (onUpgrade == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.OnUpgrade，跳过升级数值补充");
                continue;
            }
            Safe($"升级补充 {type.Name}", () => harmony.Patch(onUpgrade, postfix: upgradePostfix));
        }

        foreach (var type in Reworks.UpgradeReplace.Keys)
        {
            var onUpgrade = AccessTools.Method(type, "OnUpgrade");
            if (onUpgrade == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 {type.Name}.OnUpgrade，跳过升级整段替换");
                continue;
            }
            Safe($"升级替换 {type.Name}", () => harmony.Patch(onUpgrade, prefix: upgradePrefix));
        }

        // 关键词 / 悬停提示 / 基础费用：有的卡自己 override 了这些属性，有的没有。
        // 所以「基类声明的那一份」和「每张卡自己声明的那一份」都要打，缺一不可：
        // 只打基类 → 卡自己 override 的就盖掉了；只打卡自己 → 没 override 的（如厄运之衣）根本找不到方法，
        // Harmony 会抛 ArgumentException 并中断整个安装流程（曾经导致意图提示、卡图、血条等全部没挂上）。
        PatchProperty(harmony, keywordsPrefix, "CanonicalKeywords", Reworks.Keywords.Keys);
        PatchProperty(harmony, tipsPrefix, "ExtraHoverTips", Reworks.Tips.Keys);
        PatchProperty(harmony, costPrefix, "CanonicalEnergyCost", Reworks.Costs.Keys);

        // 改卡牌类型（攻击 → 技能之类）：CardModel.Type 是 public virtual，直接在基类 getter 上挂 postfix。
        if (Reworks.Types.Count > 0)
        {
            Safe("卡牌类型改写", () =>
            {
                var typeGetter = AccessTools.DeclaredProperty(typeof(CardModel), "Type")?.GetGetMethod(true);
                if (typeGetter == null)
                {
                    Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.Type，跳过类型改写");
                    return;
                }
                harmony.Patch(typeGetter, postfix: typePostfix);
            });
        }

        // 不再提供格挡的卡：把原版自己 override 的 GainsBlock 改回 false，
        // 否则「提升格挡值」类附魔的候选里还会列出它们。
        foreach (Type noBlockType in Reworks.NoGainsBlock)
        {
            Safe($"取消格挡标记 {noBlockType.Name}", () =>
            {
                MethodInfo? gainsBlock = AccessTools.PropertyGetter(noBlockType, "GainsBlock");
                if (gainsBlock is null)
                {
                    Main.Log.Warn($"[{Main.ModId}] {noBlockType.Name} 没有声明 GainsBlock，跳过");
                    return;
                }

                harmony.Patch(gainsBlock, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patches), nameof(Patches.GainsBlockPostfix))) { priority = postfixPriority });
            });
        }

        // 下面每一项都独立兜异常：任何一项挂载失败，都不能连累后面的功能。
        // 自制能力的图标重定向 + 为你而死的自定义图标
        Safe("能力图标", () => PowerIcons.Install(harmony));
        // 奥斯提血条 75% 不透明度
        Safe("奥斯提血条", () => OstyHpBar.Install(harmony));
        // 攻击意图数字后追加「（x）」：格挡 + 奥斯提减伤后玩家实际会掉的伤害
        Safe("意图伤害提示", () => IntentPreview.Install(harmony));
        // 自制卡牌的卡图替换
        Safe("卡图替换", () => CardArt.Install(harmony));
        // 灾厄也能杀死奥斯提（原版只结算 player/enemy 生物，宠物不在参与者名单里）
        Safe("灾厄处死奥斯提", () => OstyDoomDeath.Install(harmony));

        // 骨笛重做：每回合奥斯提第一次攻击时给 5 点格挡
        Safe("骨笛重做", () => BoneFluteRework.Install(harmony));

        // 初始命匣加强（缚魂命匣 / 无界命匣的召唤量）
        Safe("初始遗物加强", () => StarterRelicRework.Install(harmony));

        // 亡魂牵引：消耗虚无牌时改为自动打出
        Safe("亡魂牵引", () => PullFromBelowRedirect.Install(harmony));

        // 亡魂牵引：手牌里会被拦下的虚无牌亮起来
        Safe("亡魂牵引手牌提示", () => PullFromBelowGlow.Install(harmony));

        // 忧郁：你或奥斯提获得负面效果时，本场战斗永久 -1 费
        Safe("忧郁降费", () => MelancholyCost.Install(harmony));

        // 增生：「奥斯提本回合增加了多少生命值」的记录
        Safe("增生统计", () => OstyHpGainTracker.Install(harmony));

        // 钙化：奥斯提每次攻击后给玩家格挡
        Safe("钙化格挡", () => CalcifyGuard.Install(harmony));

        // 升华：抽牌加费 + 耗能变化折算伤害
        Safe("升华记账", () => SublimationTracker.Install(harmony));

        // 调试卡「飘荡实验」不进任何获取渠道
        Safe("调试卡过滤", () => TestCardFilter.Install(harmony));

        // 给我们自己新加的卡兜住 OnPlay 里的异步异常（异常会让出牌流程卡死）
        Safe("出牌异常保护", () => InstallTaskGuards(harmony));

        // 关键词自检：排查「灵魂之力」选不中某些消耗牌的问题（只打日志，不改行为）
        Safe("关键词自检", () => KeywordAudit.Install(harmony));

        // 命运同担：敌人获得力量时，奥斯提同步获得等量力量
        Safe("命运同担", () => SharedFateRework.Install(harmony));

        // 飘荡的附魔图标（原版接口只吃 res:// 里的 CompressedTexture2D，这里在卡面刷新后替换）
        Safe("飘荡图标", () => DriftIcon.Install(harmony));
        Safe("飘荡补写虚无", () => DriftPatches.InstallEnchantHook(harmony));
        Safe("飘荡关键词", () => DriftKeywords.Install(harmony));

        // 多折磨共存（恶咒 + 魂缚污染这类原版折磨原来会互相冲突直接抛异常）
        Safe("多折磨共存", () => MultiAffliction.Install(harmony));

        // 初心：记录「被动消耗的虚无牌」，供每回合回收
        Safe("被动消耗记录", () => PassiveExhaustLog.Install(harmony));
        Safe("弥合虚无保护", () => MendWard.Install(harmony));

        // 奥斯提累计伤害统计（猛晃等卡按累计值结算）
        Safe("奥斯提伤害统计", () => OstyDamageTracker.Install(harmony));

        // 本场战斗累计消耗牌数（守护者按这个加伤害）
        Safe("消耗统计", () => ExhaustCountTracker.Install(harmony));

        // 卡牌大全网格守卫（原版网格在只剩一行时会越界，导致图鉴显示不全）
        Safe("卡牌大全网格守卫", () => CardGridGuard.Install(harmony));
        Safe("动态变量保险丝", () => DynamicVarSafety.Install(harmony));
        Safe("哨卫模式强化", () => SentryModePlus.Install(harmony));

        // 死者苏生护佑：护佑期间任何人都不死（Kill 层兜底，避免 0 血却不死导致反复处死卡住）
        Safe("死者苏生护佑", () => NecroRework.Powers.ReanimateWardPower.Install(harmony));
        Safe("费用预览还原", () => CostPreview.Install(harmony));

        // 药水援助奥斯提：力量类药水可以点奥斯提（默认开启，设置页可关）
        Safe("药水援助奥斯提", () => PotionAssist.Install(harmony));

        Main.Log.Info($"[{Main.ModId}] 已挂载 {Reworks.Vars.Count} 个数值改写 / {Reworks.Play.Count} 个效果改写 / {Reworks.Upgrade.Count} 个升级补充 / {Reworks.UpgradeReplace.Count} 个升级替换 / {Reworks.Costs.Count} 个费用改写 / {Reworks.Keywords.Count} 个关键词改写 / {Reworks.Tips.Count} 个提示改写");
        Main.Log.Info(NecroSettings.ForceOverride
            ? $"[{Main.ModId}] 最高优先级已开启：本 mod 的原版卡改动会压过其它 mod 的同类改动（prefix {prefixPriority} / postfix {postfixPriority}）"
            : $"[{Main.ModId}] 最高优先级已关闭：与其它 mod 平级竞争（priority {Priority.Normal}）");

        Safe("安装冲突检查", () => ReportConflicts());
    }

    /// <summary>
    /// 检查我们依赖的几个关键方法上还有没有别的 mod 的 Harmony 补丁。
    /// Harmony 的 prefix 一旦有任何一个返回 false，排在它后面的 prefix 就都不会执行，
    /// 所以「别的 mod 也补了同一个方法」是这类功能莫名失效的常见原因。
    /// </summary>
    private static void ReportConflicts()
    {
        var targets = new (string Name, Type Type, string Member)[]
        {
            ("CardCmd.Exhaust", typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "Exhaust"),
            ("CardCmd.AutoPlay", typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "AutoPlay"),
            ("CardCmd.Enchant", typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "Enchant"),
            ("CardModel.OnPlayWrapper", typeof(CardModel), "OnPlayWrapper"),
            ("CardModel.get_Portrait", typeof(CardModel), "get_Portrait"),
            ("CardPoolModel.GetUnlockedCards", typeof(CardPoolModel), "GetUnlockedCards"),
            ("PowerModel.get_PackedIconPath", typeof(PowerModel), "get_PackedIconPath"),
            ("PowerModel.get_Icon", typeof(PowerModel), "get_Icon"),
            ("CreatureCmd.Damage", typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "Damage"),
            ("Hook.AfterSideTurnEnd", typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterSideTurnEnd"),
        };

        foreach (var (name, type, member) in targets)
        {
            Safe($"冲突检查 {name}", () =>
            {
                // 同一个名字可能有好几个重载（Enchant 就是），这里逐个查，避免 AmbiguousMatchException。
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == member)
                    .ToList();

                if (methods.Count == 0)
                {
                    Main.Log.Warn($"[{Main.ModId}] 冲突检查：找不到 {name}");
                    return;
                }

                var reported = new HashSet<string>();

                foreach (var method in methods)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var others = info.Owners
                        .Where(o => !o.StartsWith("necrorework", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (others.Count == 0) continue;

                    var key = string.Join(",", others);
                    if (!reported.Add(key)) continue;

                    Main.Log.Warn($"[{Main.ModId}] 冲突检查：{name} 上还挂着别的补丁 → {key}（prefix 数 {info.Prefixes.Count}）");
                }
            });
        }
    }

    private static void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {what} 挂载失败（已跳过，不影响其它功能）：{e}");
        }
    }

    /// <summary>
    /// 给一个虚属性挂 prefix：基类声明的那一份挂一次，表里每张卡自己声明的那一份也各挂一次。
    /// 同一个 getter 只挂一次，避免重复执行。
    /// </summary>
    private static void PatchProperty(Harmony harmony, HarmonyMethod prefix, string propertyName, IEnumerable<Type> types)
    {
        var patched = new HashSet<MethodInfo>();

        var baseGetter = AccessTools.DeclaredProperty(typeof(CardModel), propertyName)?.GetGetMethod(true);
        if (baseGetter != null)
        {
            PatchGetter(harmony, prefix, baseGetter, patched);
        }

        foreach (var type in types)
        {
            var getter = AccessTools.DeclaredProperty(type, propertyName)?.GetGetMethod(true);
            if (getter == null || getter.IsAbstract) continue;
            PatchGetter(harmony, prefix, getter, patched);
        }
    }

    private static void PatchGetter(Harmony harmony, HarmonyMethod prefix, MethodInfo getter, HashSet<MethodInfo> patched)
    {
        if (!patched.Add(getter)) return;

        try
        {
            harmony.Patch(getter, prefix: prefix);
        }
        catch (Exception e)
        {
            patched.Remove(getter);
            Main.Log.Warn($"[{Main.ModId}] 挂载 {getter.DeclaringType?.Name}.{getter.Name} 失败（跳过）：{e.Message}");
        }
    }

    /// <summary>
    /// 对我们自己写的卡牌，给 OnPlay 的返回值套一层「吃掉异常」的包装。
    /// 出新卡时不用再手动登记，反射扫本程序集里所有 CardModel 子类即可。
    /// </summary>
    private static void InstallTaskGuards(Harmony harmony)
    {
        var postfix = new HarmonyMethod(typeof(Patches), nameof(Patches.TaskGuardPostfix));
        int count = 0;

        foreach (var type in typeof(PatchInstaller).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(CardModel).IsAssignableFrom(type)) continue;

            var onPlay = AccessTools.Method(type, "OnPlay");
            if (onPlay == null) continue;

            harmony.Patch(onPlay, postfix: postfix);
            count++;
        }

        Main.Log.Info($"[{Main.ModId}] 已为 {count} 张自定义卡牌挂载出牌异常保护");
    }
}

internal static class Patches
{
    public static void VarsPostfix(object __instance, ref IEnumerable<DynamicVar> __result)
    {
        if (!NecroSettings.CardReworks) return;

        if (__instance is not CardModel card) return;
        if (!Reworks.Vars.TryGetValue(card.GetType(), out var factory)) return;

        try
        {
            __result = factory(card);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 数值改写失败: {e}");
        }
    }

    public static bool PlayPrefix(object __instance, ref Task __result, object[] __args)
    {
        if (!NecroSettings.CardReworks) return true;

        if (__instance is not CardModel card) return true;
        if (!Reworks.Play.TryGetValue(card.GetType(), out var impl)) return true;

        try
        {
            var ctx = (MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext)__args[0];
            var play = (CardPlay)__args[1];
            __result = Guarded(card, impl(card, ctx, play));
            return false; // 跳过原版实现
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 效果改写失败: {e}");
            return true;
        }
    }

    /// <summary>
    /// 兜住卡牌效果里的异步异常。
    /// 一旦 OnPlay 抛异常，出牌流程会在「把牌放进对应牌堆」之前中断，
    /// 卡牌留在 Play 堆里，界面就卡住了（表现为打出某张牌后整个战斗卡死）。
    /// 所以这里把异常吃掉并记日志，让出牌流程正常收尾。
    /// </summary>
    private static async Task Guarded(CardModel card, Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 打出时异常（已兜住，避免出牌流程卡死）: {e}");
        }
    }

    public static void UpgradePostfix(object __instance)
    {
        if (!NecroSettings.CardReworks) return;

        if (__instance is not CardModel card) return;
        if (!Reworks.Upgrade.TryGetValue(card.GetType(), out var apply)) return;

        try
        {
            apply(card);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 升级补充失败: {e}");
        }
    }

    public static bool UpgradePrefix(object __instance)
    {
        if (!NecroSettings.CardReworks) return true;

        if (__instance is not CardModel card) return true;
        if (!Reworks.UpgradeReplace.TryGetValue(card.GetType(), out var apply)) return true;

        try
        {
            apply(card);
            return false; // 跳过原版 OnUpgrade
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 升级替换失败: {e}");
            return true;
        }
    }

    public static bool KeywordsPrefix(object __instance, ref IEnumerable<CardKeyword> __result)
    {
        if (!NecroSettings.CardReworks) return true;

        if (__instance is not CardModel card) return true;
        if (!Reworks.Keywords.TryGetValue(card.GetType(), out var keywords)) return true;

        __result = keywords;
        return false;
    }

    /// <summary>
    /// <see cref="Reworks.NoGainsBlock"/> 里的卡：把 <c>GainsBlock</c> 强制改回 false。
    /// </summary>
    public static void GainsBlockPostfix(object __instance, ref bool __result)
    {
        if (!NecroSettings.CardReworks) return;

        if (!__result) return;
        if (__instance is not CardModel card) return;

        if (Reworks.NoGainsBlock.Contains(card.GetType()))
        {
            __result = false;
        }
    }

    public static bool TipsPrefix(object __instance, ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result)    {
        if (!NecroSettings.CardReworks) return true;

        if (__instance is not CardModel card) return true;
        if (!Reworks.Tips.TryGetValue(card.GetType(), out var factory)) return true;

        try
        {
            __result = factory(card);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {card.GetType().Name} 悬停提示替换失败: {e}");
            return true;
        }
    }

    /// <summary>整段替换卡牌的基础费用（原版费用写在构造函数里）。</summary>
    public static bool CostPrefix(object __instance, ref int __result)
    {
        if (!NecroSettings.CardReworks) return true;

        if (__instance is not CardModel card) return true;
        if (!Reworks.Costs.TryGetValue(card.GetType(), out int cost)) return true;

        __result = cost;
        return false;
    }

    /// <summary>整段替换卡牌类型（如把「苦难」从攻击改成技能）。</summary>
    public static void TypePostfix(object __instance, ref CardType __result)
    {
        if (!NecroSettings.CardReworks) return;

        if (__instance is not CardModel card) return;
        if (!Reworks.Types.TryGetValue(card.GetType(), out CardType type)) return;

        __result = type;
    }

    /// <summary>把自己卡牌 OnPlay 返回的 Task 换成「异常不会外泄」的版本。</summary>
    public static void TaskGuardPostfix(object __instance, ref Task __result)    {
        if (__result == null) return;
        var card = __instance as CardModel;
        string name = card?.GetType().Name ?? "?";
        __result = GuardAsync(name, __result);
    }

    private static async Task GuardAsync(string name, Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] {name} 打出时异常（已兜住，避免出牌流程卡死）: {e}");
        }
    }
}
