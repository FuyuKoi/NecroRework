using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
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

    public override PowerStackType StackType => PowerStackType.Single;

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

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;
        if (!participants.Contains(Owner)) return;

        await PowerCmd.Remove(this);
    }

    /// <summary>
    /// 兜底：把「灾厄处死」的名单里被护佑的生物剔掉。
    ///
    /// 注意不能在 <c>CreatureCmd.Kill</c> 上拦：Kill 内部会先
    /// <c>LoseHpInternal(currentHp)</c> 把血抹成 0，再问 <c>Hook.ShouldDie</c>；
    /// 如果我们让 Kill 整条失败，引擎会判定「有人在阻止最后一个生物死亡」，
    /// 直接抛 `Combat is ending, but something is continually preventing the last creature from being killed!`
    /// 并把整场战斗卡死（2026-11-23 20:37 的日志就是这么卡的）。
    /// 正确做法是让引擎压根找不到「该死」的目标。
    /// </summary>
    public static void Install(Harmony harmony)
    {
        try
        {
            MethodInfo? getDoomed = AccessTools.Method(typeof(DoomPower), "GetDoomedCreatures");

            if (getDoomed == null)
            {
                Main.Log.Info("死者苏生护佑：没找到 DoomPower.GetDoomedCreatures");
                return;
            }

            harmony.Patch(getDoomed, postfix: new HarmonyMethod(typeof(ReanimateWardPower), nameof(GetDoomedCreaturesPostfix)));
            Main.Log.Info("死者苏生护佑：已把护佑中的生物从灾厄处死名单里剔除");
        }
        catch (Exception ex)
        {
            Main.Log.Info("死者苏生护佑挂载失败：" + ex);
        }
    }

    private static void GetDoomedCreaturesPostfix(ref IReadOnlyList<Creature> __result)
    {
        if (__result == null || __result.Count == 0) return;

        List<Creature> kept = __result.Where(c => !IsWarded(c)).ToList();
        if (kept.Count == __result.Count) return;

        Main.Log.Info("死者苏生护佑：从灾厄处死名单里剔除 " + (__result.Count - kept.Count) + " 个被护佑的生物");
        __result = kept;
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
