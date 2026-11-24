using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
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

            // 真正的拦截点：所有「把牌塞进消耗堆」的路径最后都走 CardPileCmd.Add(..., PileType.Exhaust, ...)，
            // 包括回合结束时虚无牌自烧（实测它不经过 CardCmd.Exhaust）。
            var pileAdd = AccessTools.Method(typeof(CardPileCmd), "Add",
                new[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) });
            if (pileAdd != null)
            {
                harmony.Patch(pileAdd, prefix: new HarmonyMethod(typeof(PullFromBelowRedirect), nameof(ExhaustAddPrefix)));
                Main.Log.Info($"[{Main.ModId}] 亡魂牵引：已在 CardPileCmd.Add 的消耗分支上挂载拦截");
            }
            else
            {
                Main.Log.Warn($"[{Main.ModId}] 找不到 CardPileCmd.Add，亡魂牵引只能在 CardCmd.Exhaust 上拦");
            }
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引挂载失败: {e}");
        }
    }

    private static void OnPlayWrapperPrefix(CardModel __instance)
    {
        PlayingCards.Add(__instance);
        if (ReferenceEquals(__instance, _expectCard)) _expectCardPlayed = true;
    }

    /// <summary>这一发 AutoPlay 到底有没有真的把牌打出去（别的 mod 抢了 CardCmd.AutoPlay 就可能是 false）。</summary>
    private static CardModel? _expectCard;
    private static bool _expectCardPlayed;

    /// <summary>这张牌此刻正在被打出（= 主动出牌），供别的模块复用同一份判定。</summary>
    internal static bool IsBeingPlayed(CardModel card) => PlayingCards.Contains(card);

    private static void OnPlayWrapperPostfix(CardModel __instance) => PlayingCards.Remove(__instance);

    private static int _traceCount;

    private static int _pileTrace;

    /// <summary>
    /// 所有「把牌送进消耗堆」的公共漏斗。回合结束时虚无牌自烧直接走这里
    /// （不经过 CardCmd.Exhaust），所以在这一层拦截才能保证亡魂牵引一定生效。
    /// </summary>
    private static bool ExhaustAddPrefix(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (newPileType != PileType.Exhaust) return true;

            // 兜底：牌如果没登记在当前战斗里，进牌堆时会抛
            // 「must be added to a CombatState」，整条回合循环会死（结束回合卡住）。
            CombatCardGuard.EnsureRegistered(card, "消耗前兜底");

            // 「飘荡」：只要这张牌真的被送进消耗堆，就给 4 点格挡。
            // 原先挂在 CardCmd.Exhaust 上，但回合结束时虚无牌自烧不经过它
            // （直接走 CardPileCmd.Add），所以看起来「飘荡不生效」。这里统一在漏斗上处理。
            if (card.Enchantment is Enchantments.DriftEnchantment && card.Pile?.Type != PileType.Exhaust)
            {
                _ = GrantDriftBlock(card);
            }

            // 我们自己在自动打出时产生的消耗，直接放行（否则套娃）
            if (_busy) return true;

            var creature = card.Owner?.Creature;
            if (creature == null || !creature.IsAlive) return true;

            var power = creature.GetPower<PullFromBelowPower>();
            bool ethereal = card.Keywords.Contains(CardKeyword.Ethereal);

            if (_pileTrace < 40)
            {
                _pileTrace++;
                Main.Log.Info($"[{Main.ModId}] 进消耗堆#{_pileTrace} {card.Id} 牌堆={card.Pile?.Type.ToString() ?? "无"} 虚无={ethereal} 正在打出={PlayingCards.Contains(card)} 亡魂牵引={power?.Amount.ToString() ?? "无"}");
            }

            if (!ethereal) return true;
            if (PlayingCards.Contains(card)) return true;

            var pile = card.Pile?.Type;
            if (pile != PileType.Hand && pile != PileType.Draw && pile != PileType.Discard) return true;

            if (power == null || power.Amount <= 0)
            {
                // 没被亡魂牵引改成自动打出 → 这张牌真的会被消耗掉，
                // 记进「初心的被动消耗记录」（回合结束时虚无牌自烧只走这一层）。
                PassiveExhaustLog.Record(card);
                return true;
            }

            __result = RedirectAdd(card, power);
            return false;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 亡魂牵引拦截失败（放行原版消耗）: {e}");
            return true;
        }
    }

    /// <summary>CardPileCmd.Add 的返回类型是 Task&lt;CardPileAddResult&gt;（不可空），拦截后返回 default。</summary>
    private static async Task<CardPileAddResult> RedirectAdd(CardModel card, PullFromBelowPower power)
    {
        await Redirect(card, power);
        return default!;
    }

    /// <summary>「飘荡」被消耗时的 4 点格挡。</summary>
    private static async Task GrantDriftBlock(CardModel card)
    {
        try
        {
            Creature? owner = card.Owner?.Creature;
            if (owner is not { IsAlive: true }) return;

            Main.Log.Info($"[{Main.ModId}] 飘荡：{card.Id} 被消耗，获得 {Enchantments.DriftEnchantment.BlockOnExhaust} 点格挡");
            await CreatureCmd.GainBlock(owner, Enchantments.DriftEnchantment.BlockOnExhaust, ValueProp.Unpowered, null);
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 飘荡格挡失败: {e}");
        }
    }

    private static bool ExhaustPrefix(CardModel card, bool causedByEthereal, ref Task<CardPileAddResult?> __result)
    {
        try
        {
            if (_busy) return true;

            // 兜底：牌如果没登记在当前战斗里，CardCmd.Exhaust 之后会抛
            // 「must be added to a CombatState」，整条回合循环会死（结束回合卡住）。
            // 这里先补登记，保证消耗一定能走完。
            CombatCardGuard.EnsureRegistered(card, "消耗前兜底");

            var player = card.Owner;
            var creature = player?.Creature;
            if (creature == null || !creature.IsAlive) return true;

            var power = creature.GetPower<PullFromBelowPower>();
            bool ethereal = card.Keywords.Contains(CardKeyword.Ethereal);

            // 前 60 次消耗决议全部记录（不只 buff 在场时），这样能确认这个补丁到底有没有被执行。
            if (_traceCount < 200)
            {
                _traceCount++;
                Main.Log.Info($"[{Main.ModId}] 消耗决议#{_traceCount} {card.Id} 牌堆={card.Pile?.Type.ToString() ?? "无"} 虚无={ethereal} 回合结束自烧={causedByEthereal} 正在打出={PlayingCards.Contains(card)} 亡魂牵引={power?.Amount.ToString() ?? "无"}");
            }

            if (power != null && power.Amount > 0 && ethereal)
            {
                Main.Log.Info($"[{Main.ModId}] 亡魂牵引：消耗决议 {card.Id} 牌堆={card.Pile?.Type.ToString() ?? "无"} 回合结束自烧={causedByEthereal} 正在打出={PlayingCards.Contains(card)} 剩余层数={power.Amount}");
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
    /// 不经过 CardCmd.AutoPlay 的「自动打出」。复刻它最后那段流程：
    /// 可打出判定 → 选目标 → BeforeCardAutoPlayed → OnPlayWrapper。
    /// 这样即使有别的 mod 在 AutoPlay 上挂了会短路的前缀，亡魂牵引照样能生效。
    /// </summary>
    private static async Task PlayWithoutAutoPlay(CardModel card)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        var owner = card.Owner;
        ICombatState? state = card.CombatState ?? owner?.Creature?.CombatState;
        if (state == null || owner?.Creature == null || owner.Creature.IsDead) return;

        if (CombatManager.Instance.IsOverOrEnding
            || card.Keywords.Contains(CardKeyword.Unplayable)
            || !Hook.ShouldPlay(state, card, out _, AutoPlayType.Default))
        {
            await CardPileCmd.Add(card, PileType.Play);
            await card.MoveToResultPileWithoutPlaying(ctx);
            return;
        }

        Creature? target = null;
        if (card.TargetType == TargetType.AnyEnemy)
        {
            target = owner.RunState.Rng.CombatTargets.NextItem(state.HittableEnemies);
            if (target == null)
            {
                await CardPileCmd.Add(card, PileType.Play);
                await card.MoveToResultPileWithoutPlaying(ctx);
                return;
            }
        }

        await Hook.BeforeCardAutoPlayed(state, card, target, AutoPlayType.Default);

        var resources = new ResourceInfo
        {
            EnergySpent = 0,
            EnergyValue = card.EnergyCost.GetAmountToSpend(),
            StarsSpent = 0,
            StarValue = Math.Max(0, card.GetStarCostWithModifiers()),
        };

        await card.OnPlayWrapper(ctx, target, isAutoPlay: true, resources);
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

            // 牌本身带「消耗」：引擎把它送进消耗堆是正确结果，别再抢回弃牌堆
            if (pile == PileType.Exhaust)
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

            _expectCard = card;
            _expectCardPlayed = false;
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), card, null);
            bool played = _expectCardPlayed;
            _expectCard = null;

            // 别的 mod（RitsuLib / BaseLib 都给 CardCmd.AutoPlay 挂了前缀）可能把这一发跳过。
            // 单人局：自己按「打出这张牌」的最小流程再走一遍，保证亡魂牵引不依赖 AutoPlay。
            // 联机：只走引擎官方的 AutoPlay 路径；一旦它被跳过，就按原版语义直接进消耗堆，
            //       绝不在这里做只有本机执行的「自建打出」，否则两台机器会各算各的。
            if (!played || card.Pile?.Type == PileType.Play)
            {
                if (MultiplayerMode.IsMultiplayer(card))
                {
                    Main.Log.Warn($"[{Main.ModId}] 亡魂牵引（联机安全模式）：{card.Id} 的 CardCmd.AutoPlay 被跳过，按原版语义放进消耗堆");
                    await CardPileCmd.Add(card, PileType.Exhaust, CardPilePosition.Bottom);
                    return null;
                }

                Main.Log.Warn($"[{Main.ModId}] 亡魂牵引：{card.Id} 的 CardCmd.AutoPlay 被别的补丁跳过，改走自建打出流程");
                await PlayWithoutAutoPlay(card);
            }

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
