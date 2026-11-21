using MegaCrit.Sts2.Core.Combat;

namespace NecroRework;

/// <summary>
/// 「玩家点了结束回合之后，到下回合开始之前」这段冻结期。
/// 冻结期内不再重算意图红字与手牌发光，避免敌人回合里数字乱跳。
/// </summary>
internal static class FreezeGate
{
    public static bool IsFrozen()
    {
        try
        {
            var manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress)
            {
                return false;
            }

            // 玩家回合的收尾阶段（结算卡牌、状态牌、回合末效果）
            if (manager.EndingPlayerTurnPhaseOne || manager.EndingPlayerTurnPhaseTwo)
            {
                return true;
            }

            var state = manager.DebugOnlyGetState();
            return state != null && state.CurrentSide != CombatSide.Player;
        }
        catch
        {
            return false;
        }
    }
}
