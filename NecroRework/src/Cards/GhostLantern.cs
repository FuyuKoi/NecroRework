using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 冥灯：X 费技能（罕见品质）。
/// 获得 4X（升级后 4X+4）点格挡；接下来的 X（升级后 X+2）个回合，
/// 每回合抽到的前两张虚无牌会各给予 1 点费用。
/// </summary>
public sealed class GhostLantern : CardModel
{
    /// <summary>每点 X 的格挡数。</summary>
    private const decimal BlockPerX = 4m;

    /// <summary>升级后额外给的格挡（固定 +4，与 X 无关）。</summary>
    private const decimal UpgradedBlockBonus = 4m;

    /// <summary>升级后额外的持续回合数。</summary>
    private const int UpgradedTurnBonus = 2;

    public GhostLantern()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override bool HasEnergyCostX => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(BlockPerX, ValueProp.Move)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        int x = ResolveEnergyXValue();

        decimal block = x * BlockPerX + (IsUpgraded ? UpgradedBlockBonus : 0m);
        if (block > 0m)
        {
            await CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Move, cardPlay);
        }

        int turns = x + (IsUpgraded ? UpgradedTurnBonus : 0);
        if (turns > 0)
        {
            await PowerCmd.Apply<GhostLanternPower>(
                choiceContext, Owner.Creature, turns, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        // 升级效果是「固定 +4 格挡」与「+2 回合」，都在 OnPlay 里按 IsUpgraded 计算，
        // 没有可升级的 DynamicVar，所以这里留空。
    }
}
