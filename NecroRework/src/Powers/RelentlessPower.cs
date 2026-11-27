using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Powers;

/// <summary>
/// 紧追不放：挂在敌人身上的 debuff。
/// 本回合内奥斯提每「推进」2 次，就给予其 Amount 层易伤；敌方回合结束时自动移除。
/// 推进来自两处：奥斯提攻击这名敌人、以及再次打出紧追不放这张牌本身。
/// 同时维护 <see cref="SicEmCounterPower"/>（还差几下），让玩家看到倒计时。
/// </summary>
public sealed class RelentlessPower : PowerModel
{
    /// <summary>触发一次易伤需要的推进次数（也就是计数器的初始值）。</summary>
    public const int HitsPerTrigger = 2;

    /// <summary>每次给出易伤时顺带给它的召唤量（升级前后都是 5，不做叠加）。</summary>
    public const decimal SummonPerTrigger = 5m;

    [ThreadStatic] private static int _suppress;

    /// <summary>紧追不放这张牌自己打出的那一下攻击不算推进，避免一次打出掉两格。</summary>
    public static void BeginSuppress() => _suppress++;

    public static void EndSuppress()
    {
        if (_suppress > 0) _suppress--;
    }

    private static bool Suppressed => _suppress > 0;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// 悬停里的两个数字：{Amount} 是每次触发给的易伤层数（多次打出会叠），
    /// {Summon} 是每次触发的召唤量（固定 5，不随层数叠加）。
    /// </summary>
    public override LocString Description
    {
        get
        {
            int layers = Math.Max(1, Amount);
            LocString loc = new LocString("powers", Id.Entry + ".description");
            loc.Add("Amount", layers);
            loc.Add("Summon", SummonPerTrigger);
            return loc;
        }
    }

    /// <summary>悬停时把「易伤」和「召唤」两条词条也列出来。</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[2]
    {
        HoverTipFactory.FromPower<VulnerablePower>(Math.Max(1, Amount)),
        HoverTipFactory.Static(StaticHoverTip.SummonDynamic, new DynamicVar[1] { new SummonVar(SummonPerTrigger) })
    };

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer, DamageResult result,
        ValueProp props, Creature target, CardModel cardSource)
    {
        if (Suppressed) return;
        if (target != Owner) return;
        if (dealer?.Monster is not Osty osty) return;

        var petOwner = osty.Creature.PetOwner;
        if (petOwner == null || petOwner.Creature != Applier) return;

        await Advance(choiceContext);
    }

    /// <summary>
    /// 推进一次倒计时：>0 就减一并刷新显示；
    /// 减到 0 时按当前层数给易伤，并把倒计时重置回初始值。
    /// </summary>
    public async Task Advance(PlayerChoiceContext choiceContext)
    {
        var counter = Owner.GetPower<SicEmCounterPower>();

        int remaining = (counter?.Amount ?? HitsPerTrigger) - 1;

        if (remaining > 0)
        {
            counter?.SetAmount(remaining);
            return;
        }

        counter?.SetAmount(HitsPerTrigger);

        await PowerCmd.Apply<VulnerablePower>(choiceContext, Owner, Amount, Applier, null);

        // 每次施加易伤的同时给施加者召唤 5（固定值，多次打出不叠加，避免数值失控）。
        if (Applier?.Player is { } summoner)
        {
            await OstyCmd.Summon(choiceContext, summoner, SummonPerTrigger, this);
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            // 计数器和本体同寿：本体走了，倒计时也一起清掉。
            if (Owner.GetPower<SicEmCounterPower>() is { } counter)
            {
                await PowerCmd.Remove(counter);
            }

            await PowerCmd.Remove(this);
        }
    }
}
