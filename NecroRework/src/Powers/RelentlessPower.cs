using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
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

    /// <summary>每次给出易伤时顺带给它的召唤量。</summary>
    public const decimal SummonOnVulnerable = 6m;

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

        // 每次施加易伤的同时，再给施加者 6 点召唤。
        if (Applier?.Player is { } summoner)
        {
            await OstyCmd.Summon(choiceContext, summoner, SummonOnVulnerable, this);
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
