using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 联机（多人）判定 + 联机安全模式。
///
/// 设计原则：**不按「本地玩家」分支**。所有判断都基于双方都能看到的同步数据
/// （战斗里的玩家数量、以及 mod 目录下的开关文件），因此同一场战斗里两个客户端
/// 得到的结论完全一致 —— 如果这边走了「安全分支」，另一台也一定走同一个分支。
///
/// 开关：mods\NecroRework\mp_safe.flag 存在时，即使在单人局也强制按联机安全模式运行
/// （方便你自己复现/排查联机问题）。
/// </summary>
internal static class MultiplayerMode
{
    /// <summary>由 Main 在初始化时按 flag 文件设置。</summary>
    internal static bool ForcedByFlag { get; set; }

    /// <summary>是否正在进行多人战斗（≥2 名玩家）。</summary>
    public static bool IsMultiplayer(ICombatState? combatState)
    {
        if (ForcedByFlag || NecroSettings.MultiplayerSafe) return true;
        if (combatState == null) return false;

        try
        {
            return combatState.Players.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>卡牌视角的便捷重载（卡牌在战斗里时才有意义）。</summary>
    public static bool IsMultiplayer(CardModel? card) => IsMultiplayer(card?.CombatState);
}
