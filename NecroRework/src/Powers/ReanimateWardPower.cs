using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 死者苏生的护佑：本回合内不会受到任何伤害（含非攻击伤害），也不会死于灾厄。
/// 等到下个自己回合开始时才移除，这样「回合结束时的灾厄判定」一定被挡住。
/// </summary>
public sealed class ReanimateWardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>可叠加：每多打一张死者苏生就多撑一个回合（层数 = 剩余回合数）。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// 所有掉血（攻击、非攻击、生命流失）都走这里。
    /// 骨妹和她的奥斯提一起免疫 —— 否则伤害会顺着「为你而死」的分摊落到奥斯提身上。
    /// </summary>
    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        return IsProtected(target) ? 0m : amount;
    }

    /// <summary>连「处死」也挡住（灾厄致死走 CreatureCmd.Kill）。</summary>
    public override bool ShouldDie(Creature creature)
    {
        return !IsProtected(creature);
    }

    /// <summary>
    /// 护佑期间连格挡也不掉：把这一发伤害的倍率乘成 0。
    ///
    /// 一开始试的是把 <c>ModifyPreBlockDamageTarget</c> 返回 null，但引擎里
    /// <c>CreatureCmd.Damage</c> 扣格挡那一行（<c>creature.DamageBlockInternal(damageRemaining, props)</c>）
    /// 在 <c>if (preBlockTarget != null)</c> 之外，无条件执行，所以 null 只能跳过「扣格挡前的掉血」，
    /// 格挡照样被扣。改成在伤害结算最前面就把倍率乘 0：modifiedAmount 变 0 → 扣格挡 0 → 后续掉血 0。
    /// </summary>
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != null && IsProtected(target)) return 0m;
        return 1m;
    }

    /// <summary>
    /// 护佑期间灾厄叠不上去 —— 用户口径「就和灾厄没叠满一样」。
    /// 这比「叠满了但不死」安全得多：灾厄层数不会超过当前生命值，
    /// 原版/我们的灾厄处死判定（CurrentHp &lt;= 灾厄）根本不会被触发，也就不会反复 Kill 卡死。
    /// </summary>
    public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount, Creature? applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;

        if (canonicalPower is not DoomPower) return false;
        if (amount <= 0m) return false;
        if (!IsProtected(target)) return false;

        modifiedAmount = 0m;
        return true;
    }

    private bool IsProtected(Creature creature)
    {
        if (creature == Owner) return true;

        Creature? osty = Owner.Player?.Osty;
        return osty != null && ReferenceEquals(creature, osty);
    }

    /// <summary>每个自己回合开始时扣 1 层；还有层数就继续护佑，层数用完才移除。</summary>
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (!participants.Contains(Owner)) return;

        if (Amount > 1)
        {
            await PowerCmd.Decrement(this);
            return;
        }

        await PowerCmd.Remove(this);
    }

    /// <summary>
    /// 护佑期间「灾厄触发判定」整段不生效：把 DoomPower.ShouldDoomTrigger 直接拦成 false。
    ///
    /// 之前是把护佑生物从 <c>GetDoomedCreatures</c> 的返回里剔掉，但原版
    /// <c>ShouldDoomTrigger</c> 里有 <c>GetDoomedCreatures(Owner.Side).First()</c> ——
    /// 名单被剔空后 <c>First()</c> 直接抛 <c>InvalidOperationException</c>，
    /// 回合循环死掉 → 战斗卡死。改成在判定入口就返回 false，名单保持原样，谁都不会越界。
    /// </summary>
    public static void Install(Harmony harmony)
    {
        try
        {
            MethodInfo? shouldTrigger = AccessTools.Method(typeof(DoomPower), "ShouldDoomTrigger");

            if (shouldTrigger == null)
            {
                Main.Log.Info("死者苏生护佑：没找到 DoomPower.ShouldDoomTrigger");
                return;
            }

            harmony.Patch(shouldTrigger, prefix: new HarmonyMethod(typeof(ReanimateWardPower), nameof(ShouldDoomTriggerPrefix)));
            Main.Log.Info("死者苏生护佑：已在灾厄触发判定入口拦下护佑中的生物");
        }
        catch (Exception ex)
        {
            Main.Log.Info("死者苏生护佑挂载失败：" + ex);
        }
    }

    private static bool ShouldDoomTriggerPrefix(DoomPower __instance, ref bool __result)
    {
        if (!IsWarded(__instance.Owner)) return true;

        __result = false;
        return false;
    }

    /// <summary>这个生物是否正被「死者苏生」护佑（玩家自己或他的奥斯提身上挂着护佑能力）。</summary>
    internal static bool IsWarded(Creature? creature)
    {
        if (creature == null) return false;

        try
        {
            if (creature.HasPower<ReanimateWardPower>()) return true;

            Creature? owner = creature.PetOwner?.Creature;

            return owner != null && owner.HasPower<ReanimateWardPower>();
        }
        catch
        {
            return false;
        }
    }
}
