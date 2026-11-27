using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace NecroRework.Cards;

/// <summary>
/// 冥灯：X 费技能（罕见品质）。
/// 获得 4X（升级后 4X+4）点格挡——按「每段 4 点」分段结算，每段各自吃敏捷/脆弱；
/// 接下来的 X（升级后 X+2）个回合，每回合抽到的前两张虚无牌会各给予 1 点费用。
/// </summary>
public sealed class GhostLantern : CardModel
{
    /// <summary>每点 X 的格挡数（同时也是每一段的格挡值）。</summary>
    private const decimal BlockPerX = 4m;

    /// <summary>升级后额外的一段格挡（固定 +4，与 X 无关）。</summary>
    private const decimal UpgradedBlockBonus = 4m;

    /// <summary>升级后额外的持续回合数。</summary>
    private const int UpgradedTurnBonus = 2;

    public GhostLantern()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override bool HasEnergyCostX => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(BlockPerX, ValueProp.Move),
         // 只为卡面能量图标存在（每触发一次给 1 点费用），数值固定 1
         new EnergyVar(1)];

    /// <summary>格挡一共分几段（每段 4 点）：未升级 X 段，升级 X+1 段。</summary>
    private int BlockSegments(int x) => Math.Max(0, x + (IsUpgraded ? (int)(UpgradedBlockBonus / BlockPerX) : 0));

    /// <summary>持续回合数：未升级 X，升级 X+2。</summary>
    private int DurationTurns(int x) => Math.Max(0, x + (IsUpgraded ? UpgradedTurnBonus : 0));

    // 卡面写「格挡」「虚无」「能量」。
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block),
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
        HoverTipFactory.ForEnergy(this)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        int x = ResolveEnergyXValue();

        // 分段结算：每一段单独走一次 GainBlock，这样每一段都能各自吃敏捷/脆弱。
        int segments = BlockSegments(x);
        for (int i = 0; i < segments; i++)
        {
            await CreatureCmd.GainBlock(Owner.Creature, BlockPerX, ValueProp.Move, cardPlay);
        }

        int turns = DurationTurns(x);
        if (turns > 0)
        {
            await PowerCmd.Apply<GhostLanternPower>(
                choiceContext, Owner.Creature, turns, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        // 升级效果是「多一段格挡」与「+2 回合」，都在 OnPlay 里按 IsUpgraded 计算，
        // 没有可升级的 DynamicVar，所以这里留空。
    }

    /// <summary>
    /// 战斗内追加「（获得？点格挡）」与「（持续？个回合）」两处预览；百科（canonical）里两处都为空。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        // 只给「真正在手里、且战斗进行中」的牌加读数：
        // 牌组/抽牌堆界面里牌不在手上，且此时去算格挡会走战斗钩子（日志里出现过
        // Hook.ModifyXValue → IterateCombatHookListeners 的空引用，把牌组界面搞成占位图）。
        bool inCombat = !IsCanonical
            && Owner?.PlayerCombatState != null
            && CombatManager.Instance.IsInProgress
            && Pile?.Type == PileType.Hand;
        if (!inCombat)
        {
            description.Add("BlockReadout", string.Empty);
            description.Add("TurnReadout", string.Empty);
            return;
        }

        try
        {
            AddReadouts(description);
        }
        catch (Exception e)
        {
            // 任何异常都只是让这两个括号不显示，绝不能让卡面崩掉
            description.Add("BlockReadout", string.Empty);
            description.Add("TurnReadout", string.Empty);
            Main.Log.Warn($"[{Main.ModId}] 冥灯读数计算失败（已忽略）：{e.Message}");
        }
    }

    /// <summary>真正计算并写入两个读数（只在战斗内的手牌上调用）。</summary>
    private void AddReadouts(LocString description)
    {

        // 卡面预览用「当前可用能量」当作 X：ResolveEnergyXValue() 只在真正打出时有意义，
        // 在手里预览时它返回 0，会让预览永远显示成最低档。
        int x = Owner?.PlayerCombatState?.Energy ?? 0;
        int segments = BlockSegments(x);
        int turns = DurationTurns(x);

        int total = 0;
        if (segments > 0 && CombatState != null)
        {
            // 每段都按引擎的格挡公式算一次（含敏捷/脆弱/减益），再乘段数。
            decimal per = Hook.ModifyBlock(CombatState, Owner.Creature, BlockPerX, ValueProp.Move, this, null, out _);
            total = (int)Math.Round(segments * per, MidpointRounding.AwayFromZero);
        }

        description.Add("BlockReadout", $"\n[green]（获得{total}点格挡）[/green]");
        description.Add("TurnReadout", $"[green]（持续{turns}个回合）[/green]");
    }
}
