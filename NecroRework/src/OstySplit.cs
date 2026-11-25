using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

using NecroRework.Settings;
namespace NecroRework;

/// <summary>
/// 重做「为你而死」：奥斯提只吸收 50% 的未格挡攻击伤害，剩下的仍然打在玩家身上。
///
/// 实现思路（v0.4.7 重写，避免"伤害被算两次"）：
///   1. 原版会把「未格挡伤害」整包转给奥斯提。我们不让它转别处，而是在结算这一步把
///      这一发分成两半：**玩家那一半直接交给引擎原本的扣血调用**（格挡、易伤、数字、
///      孤注一掷全部走原版那条唯一路径），所以我们只返回 playerShare 当结果。
///   2. 奥斯提那一半在这次伤害整体结束后，由我们自己补一发真实的伤害调用，
///      并明确传入 ModifyDamageHookType.None —— 因为这一半的数值已经是"最终值"，
///      如果让力量/易伤再乘一次，玩家就会被打出远超预期的伤害（旧版本的问题）。
///
/// 注意：包装 Damage 必须防重入，否则会无限递归 → 栈溢出 → 游戏崩溃。
/// </summary>
internal static class OstySplit
{
    private struct Split
    {
        public Creature Osty;
        public decimal OstyShare;
        public ValueProp Props;
        public Creature Dealer;
        public CardModel CardSource;
        public string HitVfx;
        public bool VfxOnCenter;
    }

    /// <summary>
    /// 应急开关：mod 目录下放一个 no_osty_split.flag 就整体关闭 50% 分摊（其它功能不受影响）。
    /// </summary>
    private static bool _disabledByFlag;

    /// <summary>设置页的「奥斯提分摊一半伤害」+ 应急 flag 文件共同决定是否生效。</summary>
    internal static bool Enabled
    {
        get => NecroSettings.OstySplit && !_disabledByFlag;
        set => _disabledByFlag = !value;
    }

    private static readonly List<Split> Pending = new();

    /// <summary>正在补结算我们自己的奥斯提伤害：禁止再次分摊。</summary>
    private static bool _applyingShare;

    /// <summary>Damage 包装的重入深度。>0 表示正处在自己的包装里，必须直接走原版。</summary>
    private static int _depth;

    /// <summary>
    /// 本次伤害里「已经被标记要分摊」的玩家生物。
    ///
    /// 多人局必须用集合而不是单个标记：`Hook.ModifyUnblockedDamageTarget` 会把**所有**监听者都调一遍，
    /// 而且两个玩家的奥斯提各有一个 `DieForYouPower`。以前这里是全局单个 `_armed`，第二个玩家的
    /// `DieForYouPower` 一进来就把标记清掉了 → 只有一个人的奥斯提会承伤。
    /// </summary>
    private static readonly HashSet<Creature> ArmedPlayers = new();

    /// <summary>本次伤害里每个目标原本的命中特效，用来让我们自己结算的那一段也播特效。</summary>
    private static Dictionary<Creature, (string Vfx, bool Center)> _vfxMap = new();
    private static ModifyDamageHookType _currentHookType = ModifyDamageHookType.All;

    // ---------------- 1. 拦截转移，并标记这一发要分摊 ----------------

    [HarmonyPatch(typeof(DieForYouPower), nameof(DieForYouPower.ModifyUnblockedDamageTarget))]
    private static class MarkRedirect
    {
        [HarmonyPrefix]
        private static bool Prefix(DieForYouPower __instance, Creature target, ValueProp props, ref Creature __result)
        {
            if (!Enabled) return true;
            if (_applyingShare)
            {
                __result = target;
                return false;
            }

            var osty = __instance.Owner;
            if (osty == null || !osty.IsAlive) return true;
            if (target != osty.PetOwner?.Creature) return true;
            if (!props.IsPoweredAttack()) return true;

            // 「吸引仇恨」期间不分摊：让原版把整发伤害都交给奥斯提
            if (osty.HasPower<OstyGuardPower>()) return true;

            // 不交给原版转移：标记后由我们在 AfterOsty 阶段把这一发拆成两半。
            // 注意这里**不能**清空别人的标记（多人局里另一个玩家的奥斯提也会走到这个前缀）。
            ArmedPlayers.Add(target);
            __result = target;
            return false;
        }
    }

    // ---------------- 2. 把这一发拆成两半：玩家那半交回引擎，奥斯提那半记下来 ----------------

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.ModifyHpLostAfterOsty))]
    private static class SplitHpLoss
    {
        [HarmonyPrefix]
        private static bool Prefix(AbstractModel __instance, Creature target, decimal amount, ValueProp props,
                                   Creature dealer, CardModel cardSource, ref decimal __result)
        {
            if (!ArmedPlayers.Contains(target)) return true;
            if (!props.IsPoweredAttack()) return true;

            ArmedPlayers.Remove(target);

            var player = target;
            if (player == null)
            {
                __result = amount;
                return true;
            }

            var osty = player.Player?.Osty;

            // 取整规则（用户 2026-11-23 定）：
            //   整数伤害 → 奥斯提那份向上取整（3 → 2/1、5 → 3/2），保留「奥斯提多担一点」的手感；
            //   带小数的伤害（易伤×虚弱把 2 变成 2.25 这种）→ 两边精确对半，否则 ceil 会把几乎全部推给奥斯提；
            //   任何情况下份额都不超过本次未格挡伤害、也不为负
            //   （曾经 0.250 → 奥斯提 1 / 玩家 −0.75，玩家反而回血）。
            var ostyShare = 0m;
            if (osty is { IsAlive: true })
            {
                var half = amount / 2m;
                var wholeDamage = amount == Math.Floor(amount);
                ostyShare = Math.Min(wholeDamage ? Math.Ceiling(half) : half, amount);
                ostyShare = Math.Min(ostyShare, osty.CurrentHp);
            }

            if (ostyShare < 0m) ostyShare = 0m;

            var playerShare = amount - ostyShare;

            if (ostyShare > 0m && osty != null)
            {
                _vfxMap.TryGetValue(player, out var vfx);

                Pending.Add(new Split
                {
                    Osty = osty,
                    OstyShare = ostyShare,
                    Props = props,
                    Dealer = dealer,
                    CardSource = cardSource,
                    HitVfx = vfx.Vfx,
                    VfxOnCenter = vfx.Center,
                });

                Main.Log.Info(
                    $"[{Main.ModId}] 分摊：未格挡 {amount} → 奥斯提 {ostyShare}（HP {osty.CurrentHp}→{osty.CurrentHp - ostyShare}）" +
                    $"／玩家 {playerShare}（HP {player.CurrentHp}→{player.CurrentHp - playerShare}）");
            }

            // 玩家那一半交给引擎原本的扣血流程：格挡已经在上面扣过，这里只走最后一次 LoseHpInternal
            __result = playerShare;
            return false;
        }
    }

    // ---------------- 3. 整次伤害结束后，补一发只打奥斯提的伤害 ----------------

    [HarmonyPatch]
    private static class ApplyPendingAfterDamage
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            // 所有 Damage 重载最终都会汇到这一个
            var exact = AccessTools.Method(typeof(CreatureCmd), "Damage", new[]
            {
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<DamageTargetData>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
                typeof(CardPlay),
                typeof(ModifyDamageHookType),
            });

            if (exact != null) return exact;

            var fallback = AccessTools.GetDeclaredMethods(typeof(CreatureCmd))
                .FirstOrDefault(m => m.Name == "Damage" && m.GetParameters().Length == 8);

            if (fallback == null)
                Main.Log.Error($"[{Main.ModId}] 找不到 CreatureCmd.Damage 的 8 参数重载，奥斯提 50% 分摊不会生效");

            return fallback;
        }

        private static bool Prefix(object[] __args, ref Task<IEnumerable<DamageResult>> __result)
        {
            if (!Enabled) return true;
            // 重入保护：自己的包装执行期间，一律直接走原版
            if (_depth > 0 || _applyingShare) return true;
            if (!HasOstyPlayer(__args)) return true;

            __result = Run(__args);
            return false;
        }

        private static bool HasOstyPlayer(object[] args)
        {
            if (args.Length < 8) return false;
            if (args[1] is not IEnumerable<DamageTargetData> targetData) return false;
            if (args[3] is not ValueProp props || !props.IsPoweredAttack()) return false;

            foreach (var d in targetData)
            {
                var c = d.target;
                if (c?.Player?.Osty is { IsAlive: true }) return true;
            }
            return false;
        }

        private static Dictionary<Creature, (string Vfx, bool Center)> BuildVfxMap(IEnumerable<DamageTargetData> targetData)
        {
            var map = new Dictionary<Creature, (string, bool)>();
            if (targetData == null) return map;

            foreach (var d in targetData)
            {
                if (d.target == null) continue;
                map[d.target] = (d.hitVfx, d.playVfxOnCreatureCenter);
            }
            return map;
        }

        private static async Task<IEnumerable<DamageResult>> Run(object[] args)
        {
            _depth++;
            try
            {
                var ctx = (PlayerChoiceContext)args[0];
                var targetData = (IEnumerable<DamageTargetData>)args[1];
                var amount = (decimal)args[2];
                var props = (ValueProp)args[3];
                var dealer = (Creature)args[4];
                var cardSource = (CardModel)args[5];
                var cardPlay = (CardPlay)args[6];
                var hookType = (ModifyDamageHookType)args[7];

                var previousVfx = _vfxMap;
                var previousHookType = _currentHookType;

                _vfxMap = BuildVfxMap(targetData);
                _currentHookType = hookType;
                ArmedPlayers.Clear();
                Pending.Clear();

                IEnumerable<DamageResult> result;
                try
                {
                    // _depth > 0，这次调用会直接命中重入保护，走原版实现
                    result = await CreatureCmd.Damage(ctx, targetData, amount, props, dealer, cardSource, cardPlay, hookType);
                }
                finally
                {
                    _vfxMap = previousVfx;
                    _currentHookType = previousHookType;
                }

                if (Pending.Count == 0) return result;

                var splits = new List<Split>(Pending);
                Pending.Clear();

                _applyingShare = true;
                try
                {
                    foreach (var s in splits)
                    {
                        if (s.OstyShare <= 0m || s.Osty is not { IsAlive: true }) continue;

                        var ostyData = new DamageTargetData(s.Osty)
                        {
                            hitVfx = s.HitVfx,
                            playVfxOnCreatureCenter = s.VfxOnCenter,
                        };

                        // 数值已经是最终值，不能再让力量／易伤乘一次，所以传 None
                        await CreatureCmd.Damage(ctx, new[] { ostyData }, s.OstyShare,
                            s.Props, s.Dealer, s.CardSource, null, ModifyDamageHookType.None);
                    }
                }
                catch (Exception e)
                {
                    Main.Log.Error($"[{Main.ModId}] 结算奥斯提分摊伤害失败: {e}");
                }
                finally
                {
                    _applyingShare = false;
                }

                return result;
            }
            catch (Exception e)
            {
                Main.Log.Error($"[{Main.ModId}] 奥斯提伤害分摊失败: {e}");
                return Array.Empty<DamageResult>();
            }
            finally
            {
                _depth--;
            }
        }
    }
}
