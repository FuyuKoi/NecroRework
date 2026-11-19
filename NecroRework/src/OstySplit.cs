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

namespace NecroRework;

/// <summary>
/// 重做「为你而死」：奥斯提只吸收 50% 的未格挡攻击伤害，剩下的仍然打在玩家身上。
///
/// 实现思路（结构性防重复）：
///   1. 原版会把「未格挡伤害」整包转给奥斯提。这里**不让它转**，改为把这一发伤害的数值清成 0，
///      所以引擎自己不会给任何一方扣血，不存在"引擎扣一次 + 我再补一次"。
///   2. 在这一发伤害整体结束后，我自己用两次 CreatureCmd.Damage 结算：
///        奥斯提吃 min(ceil(未格挡/2), 奥斯提当前血量)
///        玩家吃   未格挡 - 奥斯提那份
///      两者都用完整伤害管线（会弹数字、会触发受击效果），且总额恒等于未格挡伤害。
///
/// 注意：包装 Damage 必须防重入，否则会无限递归 → 栈溢出 → 游戏崩溃。
/// </summary>
internal static class OstySplit
{
    private struct Split
    {
        public Creature Player;
        public Creature Osty;
        public decimal OstyShare;
        public decimal PlayerShare;
        public ValueProp Props;
        public Creature Dealer;
        public CardModel CardSource;
        public ModifyDamageHookType HookType;
        public string HitVfx;
        public bool VfxOnCenter;
    }

    /// <summary>
    /// 应急开关：mod 目录下放一个 no_osty_split.flag 就整体关闭 50% 分摊（其它功能不受影响）。
    /// </summary>
    internal static bool Enabled = true;

    private static readonly List<Split> Pending = new();

    private static PlayerChoiceContext _ctx;

    /// <summary>正在补结算我们自己的两次伤害：禁止再次分摊 / 再次转移。</summary>
    private static bool _applyingShare;

    /// <summary>Damage 包装的重入深度。>0 表示正处在自己的包装里，必须直接走原版。</summary>
    private static int _depth;

    /// <summary>本次伤害已被标记为「要分摊」，等待在 AfterOsty 阶段消费。</summary>
    private static bool _armed;
    private static Creature _armedPlayer;

    /// <summary>本次伤害里每个目标原本的命中特效，用来让我们自己结算的那两段也播特效。</summary>
    private static Dictionary<Creature, (string Vfx, bool Center)> _vfxMap = new();
    private static ModifyDamageHookType _currentHookType = ModifyDamageHookType.All;

    // ---------------- 1. 拦截转移，并标记这一发要分摊 ----------------

    [HarmonyPatch(typeof(DieForYouPower), nameof(DieForYouPower.ModifyUnblockedDamageTarget))]
    private static class MarkRedirect
    {
        [HarmonyPrefix]
        private static bool Prefix(DieForYouPower __instance, Creature target, ValueProp props, ref Creature __result)
        {
            _armed = false;

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

            // 不交给原版转移：标记后让引擎这一发伤害变成 0，稍后由我们自己结算
            _armed = true;
            _armedPlayer = target;
            __result = target;
            return false;
        }
    }

    // ---------------- 2. 把引擎这一发伤害清零，并记下两边份额 ----------------

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.ModifyHpLostAfterOsty))]
    private static class SplitHpLoss
    {
        [HarmonyPrefix]
        private static bool Prefix(AbstractModel __instance, Creature target, decimal amount, ValueProp props,
                                   Creature dealer, CardModel cardSource, ref decimal __result)
        {
            if (!_armed) return true;
            if (target != _armedPlayer) return true;
            if (!props.IsPoweredAttack()) return true;

            _armed = false;

            var player = _armedPlayer;
            var osty = player?.Player?.Osty;

            // 奥斯提那份向上取整，且不能超过当前血量；超出部分自然落回玩家
            var ostyShare = 0m;
            if (osty is { IsAlive: true })
                ostyShare = Math.Min(Math.Ceiling(amount / 2m), osty.CurrentHp);

            var playerShare = amount - ostyShare;

            if (amount > 0m && player != null)
            {
                _vfxMap.TryGetValue(player, out var vfx);

                Pending.Add(new Split
                {
                    Player = player,
                    Osty = ostyShare > 0m ? osty : null,
                    OstyShare = ostyShare,
                    PlayerShare = playerShare,
                    Props = props,
                    Dealer = dealer,
                    CardSource = cardSource,
                    HookType = _currentHookType,
                    HitVfx = vfx.Vfx,
                    VfxOnCenter = vfx.Center,
                });

                Main.Log.Info($"[{Main.ModId}] 分摊：未格挡 {amount} → 奥斯提 {ostyShare}（HP {osty?.CurrentHp ?? 0}）／玩家 {playerShare}");
            }

            // 引擎这一发不再扣血，避免重复结算
            __result = 0m;
            return false;
        }
    }

    // ---------------- 3. 整次伤害结束后，用真实伤害管线分别结算 ----------------

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

                var previousCtx = _ctx;
                var previousVfx = _vfxMap;
                var previousHookType = _currentHookType;

                _ctx = ctx;
                _vfxMap = BuildVfxMap(targetData);
                _currentHookType = hookType;
                _armed = false;
                Pending.Clear();

                IEnumerable<DamageResult> result;
                try
                {
                    // _depth > 0，这次调用会直接命中重入保护，走原版实现（引擎那一发已被清零）
                    result = await CreatureCmd.Damage(ctx, targetData, amount, props, dealer, cardSource, cardPlay, hookType);
                }
                finally
                {
                    _ctx = previousCtx;
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
                        if (s.OstyShare > 0m && s.Osty is { IsAlive: true })
                        {
                            var ostyData = new DamageTargetData(s.Osty)
                            {
                                hitVfx = s.HitVfx,
                                playVfxOnCreatureCenter = s.VfxOnCenter,
                            };
                            await CreatureCmd.Damage(ctx, new[] { ostyData }, s.OstyShare,
                                s.Props, s.Dealer, s.CardSource, null, s.HookType);
                        }

                        if (s.PlayerShare > 0m)
                        {
                            var playerData = new DamageTargetData(s.Player)
                            {
                                hitVfx = s.HitVfx,
                                playVfxOnCreatureCenter = s.VfxOnCenter,
                            };
                            await CreatureCmd.Damage(ctx, new[] { playerData }, s.PlayerShare,
                                s.Props, s.Dealer, s.CardSource, null, s.HookType);
                        }
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
