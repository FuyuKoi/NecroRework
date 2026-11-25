using STS2RitsuLib.Settings;

namespace NecroRework.Settings;

/// <summary>
/// RitsuLib 属性驱动的设置页。RitsuLib 会自动扫描已加载程序集里带 [ModSettingsPage] 的类，
/// 我们不需要手动注册；每个开关的取值通过 ModDataStore 持久化，改完立刻写回静态属性。
/// 全部默认开启（关掉「重置卡牌」= 原版卡原样，关掉「拓展卡牌」= 自制卡不进池）。
/// </summary>
[ModSettingsPage("NecroRework", ModDisplayName = "Fuyu的亡灵契约师重置", SortOrder = 100,
    Title = "Fuyu的亡灵契约师重置", Description = "骨妹的卡牌重制与新卡。所有开关默认开启，改动即时生效（少数需要重开一局战斗）。")]
[ModSettingsSection("cards", Title = "卡牌", Description = "整体开关，决定这一局里看到的是哪一套卡。", SortOrder = 10)]
[ModSettingsSection("combat", Title = "战斗机制", Description = "会影响打法的机制开关。", SortOrder = 20, IsCollapsible = true, StartCollapsed = true)]
[ModSettingsSection("ui", Title = "界面", Description = "只影响显示，不影响平衡。", SortOrder = 30, IsCollapsible = true, StartCollapsed = true)]
[ModSettingsSection("multiplayer", Title = "联机", Description = "多人局相关的兼容选项。", SortOrder = 40, IsCollapsible = true, StartCollapsed = true)]
[ModSettingsSection("combo", Title = "联动", Description = "和其他 mod 一起安装时的隐藏内容。", SortOrder = 50, IsCollapsible = true, StartCollapsed = true)]
public static class NecroSettings
{
    /// <summary>重置卡牌：开启时原版卡按本 mod 的重制数值/效果运行；关闭则原版卡完全保持原样。</summary>
    [ModSettingsToggle("card_reworks", "cards", Order = 10,
        Label = "重置卡牌",
        Description = "开启：原版卡使用重制后的数值与效果。关闭：原版卡全部保持未改动状态（自制卡仍在，如果你没关「拓展卡牌」）。")]
    public static bool CardReworks { get; set; } = true;

    /// <summary>拓展卡牌：开启时 mod 新增的卡会进入亡灵契约师卡池。</summary>
    [ModSettingsToggle("extra_cards", "cards", Order = 20,
        Label = "拓展卡牌",
        Description = "开启：mod 新增的卡牌会出现在卡牌奖励里。关闭：只有原版卡（已经在新卡上的存档不受影响）。")]
    public static bool ExtraCards { get; set; } = true;

    /// <summary>最高优先级：压过其他 mod 对骨妹原版卡的改动（别人新加的卡不受影响）。</summary>
    [ModSettingsToggle("force_override", "cards", Order = 30,
        Label = "最高优先级（覆盖其他 mod 的原版卡改动）",
        Description = "开启：本 mod 对亡灵契约师原版卡的数值 / 效果 / 升级 / 关键词 / 费用会强制压过其他 mod 的同类改动；其他 mod 新增的卡完全不受影响。关闭：与其它 mod 平级竞争。修改后需重开游戏生效。")]
    public static bool ForceOverride { get; set; } = true;

    /// <summary>奥斯提 50% 分摊：关闭后「为你而死」回到原版的全额吸收。</summary>
    [ModSettingsToggle("osty_split", "combat", Order = 10,
        Label = "奥斯提分摊一半伤害",
        Description = "开启：奥斯提只吸收 50% 未被格挡的伤害（向上取整），剩下的由你承受。关闭：回到原版全额吸收。")]
    public static bool OstySplit { get; set; } = true;

    /// <summary>联机安全模式：多人局里把可能不同步的实现换成保守做法。</summary>
    [ModSettingsToggle("mp_safe", "multiplayer", Order = 10,
        Label = "联机安全模式",
        Description = "多人局里自动启用：亡魂牵引不再自行重打被别的补丁跳过的牌、忧郁降费按回合去重、多重侵蚀只保留第一层。单人局不受影响。")]
    public static bool MultiplayerSafe { get; set; } = true;

    /// <summary>意图预判数字：敌人攻击意图右侧/上方的「（实际伤害）」提示。</summary>
    [ModSettingsToggle("intent_preview", "ui", Order = 10,
        Label = "意图伤害预判",
        Description = "开启：在敌人攻击意图上显示你实际会吃到的伤害（红色）以及奥斯提会吃到的部分（青色）。")]
    public static bool IntentPreview { get; set; } = true;

    /// <summary>卡图替换：自制卡使用我们提供的卡面，关闭则用原版的占位图。</summary>
    [ModSettingsToggle("card_art", "ui", Order = 20,
        Label = "自制卡图",
        Description = "开启：mod 新增卡使用专属卡面。关闭：显示原版的占位图（BETA）。")]
    public static bool CardArt { get; set; } = true;

    /// <summary>
    /// 申必梦幻联动？！—— 目前**没有任何实际效果**，纯粹是给后续「联动」功能占位的开关。
    /// 等某条联动真正实装后，再让它参与判定（或者直接改成按检测到的 mod 自动启用）。
    /// </summary>
    [ModSettingsToggle("combo_teaser", "combo", Order = 10,
        Label = "申必梦幻联动？！",
        Description = "和以下 mod 一起安装时，会解锁隐藏的重做与加强：《初始遗物进化！》。后续还会加入更多联动对象。目前只是个预告，开关暂时不改变任何东西。")]
    public static bool ComboTeaser { get; set; } = true;
}
