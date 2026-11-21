using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Powers;

namespace NecroRework;

/// <summary>
/// 「亡魂牵引」的拦截逻辑。
///
/// 游戏里所有把牌送进消耗堆的路径最后都会走 CardCmd.Exhaust：
///   · 回合结束时虚无牌自我消耗（CombatManager → CardCmd.Exhaust(causedByEthereal: true)）
///   · 洁净这类在抽牌堆里消耗
///   · 散射炮这类在弃牌堆里消耗
///   · 燃烧契约/真功夫这类在手牌里消耗
/// 所以只在 CardCmd.Exhaust 上拦一次就够了。
///
/// 拦截条件：这张牌带「虚无」，且出牌者身上还有亡魂牵引层数，且这张牌在 手牌/抽牌堆/弃牌堆 里
/// （在 Play 堆里的牌说明它正在被打出，这时再自动打出会变成打两次，跳过）。
/// 拦截后的处理：把这张牌自动打出一次，结果位置由 PullFromBelowPower 改成弃牌堆，
/// 打完之后消耗一层亡魂牵引。
/// </summary>
internal static class PullFromBelowRedirect
{
    /// <summary>正在被我们自动打出的牌（PullFromBelowPower 用它判断要不要改结果位置）。</summary>
    internal static CardModel? CurrentCard;

    /// <summary>防止自动打出过程中触发的消耗再次被拦（会无限套娃）。</summary>
    [ThreadStatic] private static bool _busy;

    /// <summary>
    /// 正在「被打出」的牌。手动打出一张带「消耗」的虚无牌时，消耗是打出流程的最后一步，
    /// 这时牌虽然已经离开 Play 堆，但它是被玩家主动打出去的 —— 不算「被动消耗」，不能拦。
    /// </summary>
    private static readonly HashSet<CardModel> PlayingCards = new(ReferenceEqualityComparer.Instance);

    public static void Install(Harmony harmony)
    {
        try
        {
            var exhaust = AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Exhaust));
            if (exhaust == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardCmd.Exhaust，亡魂牵引的「阻止消耗」未生效");
                return;
            }

            harmony.Patch(exhaust, prefix: new HarmonyMethod(typeof(PullFromBelowRedirect), nameof(ExhaustPrefix)));

            var onPlayWrapper = AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper));
            if (onPlayWrapper == null)
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardModel.OnPlayWrapper，亡魂牵引可能误拦手动打出的牌");
            }
            else
            {
                harmony.Patch(onPlayWrapper,
                    prefix: new HarmonyMethod(typeof(PullFromBelowRedirect), nameof(OnPlayWrapperPrefix)),
                    postfix: new HarmonyMethod(typeof(PullFromBelowRedirect), nameof(OnPlayWrapperPostfix)));
            }

            Main.Log.Info($"[{Main.ModId}] 亡魂牵引已挂载：被动消耗虚无牌时改为自动打出");
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引挂载失败: {e}");
        }
    }

    private static void OnPlayWrapperPrefix(CardModel __instance) => PlayingCards.Add(__instance);

    private static void OnPlayWrapperPostfix(CardModel __instance) => PlayingCards.Remove(__instance);

    private static bool ExhaustPrefix(CardModel card, bool causedByEthereal, ref Task<CardPileAddResult?> __result)
    {
        try
        {
            if (_busy) return true;

            var player = card.Owner;
            var creature = player?.Creature;
            if (creature == null || !creature.IsAlive) return true;

            var power = creature.GetPower<PullFromBelowPower>();
            bool ethereal = card.Keywords.Contains(CardKeyword.Ethereal);

            // 身上有亡魂牵引时，把每次「消耗决议」都记下来，方便排查到底是被谁吃掉的。
            if (power != null && power.Amount > 0)
            {
                Main.Log.Info($"[{Main.ModId}] 亡魂牵引：消耗决议 {card.Id} 牌堆={card.Pile?.Type.ToString() ?? "无"} 虚无={ethereal} 回合结束自烧={causedByEthereal} 正在打出={PlayingCards.Contains(card)} 剩余层数={power.Amount}");
            }

            if (!ethereal) return true;

            // 正在被打出的牌（手动打出 + 自身带消耗）→ 这是主动消耗，放行原版
            if (PlayingCards.Contains(card)) return true;

            var pile = card.Pile?.Type;
            if (pile != PileType.Hand && pile != PileType.Draw && pile != PileType.Discard) return true;

            if (power == null || power.Amount <= 0) return true;

            __result = Redirect(card, power);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引拦截失败（放行原版消耗）: {e}");
            return true;
        }
    }

    /// <summary>
    /// 自动打出结束后，保证技能/攻击牌落在弃牌堆里。
    /// 能力牌不在此列：它和手动打出时一样只生效、不留在任何牌堆里。
    /// </summary>
    private static async Task EnsureInDiscard(CardModel card)
    {
        try
        {
            if (card.Type == CardType.Power)
            {
                Main.Log.Info($"[{Main.ModId}] 亡魂牵引：{card.Id}（能力牌）已自动打出，按原版离场");
                return;
            }

            var pile = card.Pile?.Type;
            Main.Log.Info($"[{Main.ModId}] 亡魂牵引：{card.Id} 自动打出结束，牌堆 = {pile?.ToString() ?? "无"}");

            if (pile == PileType.Discard)
            {
                return;
            }

            var result = await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Bottom);
            if (!result.success)
            {
                Main.Log.Warn($"[{Main.ModId}] 亡魂牵引：{card.Id} 放回弃牌堆失败（牌已被移出战斗？）");
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引：{card.Id} 放回弃牌堆异常: {e}");
        }
    }

    private static async Task<CardPileAddResult?> Redirect(CardModel card, PullFromBelowPower power)
    {
        _busy = true;
        CurrentCard = card;
        try
        {
            Main.Log.Info($"[{Main.ModId}] 亡魂牵引：{card.Id} 改为自动打出");

            power.FlashNow();
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), card, null);

            // 能力牌被打出后，引擎默认把它从战斗里摘掉（结果堆是 None），
            // 所以这里补一次：只要它还没进弃牌堆，就手动放进去。
            await EnsureInDiscard(card);

            return null;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引自动打出 {card.Id} 失败: {e}");
            return null;
        }
        finally
        {
            CurrentCard = null;
            _busy = false;

            try
            {
                await PowerCmd.Decrement(power);
            }
            catch (Exception e)
            {
                Main.Log.Error($"[{Main.ModId}] 亡魂牵引层数扣减失败: {e}");
            }
        }
    }
}
