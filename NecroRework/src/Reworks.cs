using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace NecroRework;

/// <summary>
/// 所有对原版卡的改动都集中在这张表里。
/// 加一张卡 = 加一条；改数值 = 改一个数字。不需要动补丁代码。
/// </summary>
internal static class Reworks
{
    /// <summary>
    /// 只改数值：卡牌类型 -> 新的 CanonicalVars。
    /// 描述里的 {Summon:diff()} / {Damage:diff()} 会自动跟着变，所以不用改文案。
    /// </summary>
    public static readonly Dictionary<Type, Func<CardModel, IEnumerable<DynamicVar>>> Vars = new()
    {
        // 护卫：召唤 5（升级 7）→ 召唤 8（升级 10）
        [typeof(Bodyguard)] = _ => [new SummonVar(8m)],
    };

    /// <summary>
    /// 改效果：卡牌类型 -> 新的 OnPlay 实现。
    /// </summary>
    public static readonly Dictionary<Type, Func<CardModel, PlayerChoiceContext, CardPlay, Task>> Play = new()
    {
        // 例：
        // [typeof(BlightStrike)] = async (card, ctx, play) =>
        // {
        //     await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
        //         .FromCard(card, play)
        //         .Targeting(play.Target)
        //         .Execute(ctx);
        // },
    };
}
