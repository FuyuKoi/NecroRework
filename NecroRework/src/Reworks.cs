using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;
using NecroRework.Stats;
using NecroRework.Cards;
using NecroRework.Enchantments;

namespace NecroRework;

/// <summary>
/// 所有对原版卡的改动都集中在这几张表里。
/// 加一张卡 = 加一条；改数值 = 改一个数字。不需要动补丁代码。
/// </summary>
internal static class Reworks
{
    /// <summary>
    /// 只改数值 / 数值公式：卡牌类型 -> 新的 CanonicalVars。
    /// 描述里的 {Summon:diff()} 之类会自动跟着变，所以不用改文案。
    /// </summary>
    public static readonly Dictionary<Type, Func<CardModel, IEnumerable<DynamicVar>>> Vars = new()
    {
        // 护卫：召唤 5（升级 7）→ 召唤 8（升级 10）
        [typeof(Bodyguard)] = _ => [new SummonVar(8m)],

        // 出击：1 费，奥斯提造成 6（升级 9）点伤害；
        //       本场战斗中每获得过 2 点召唤，额外 +1 伤害。
        //       伤害 = CalculationBase(6) + ExtraDamage(1) × floor(累计召唤 / 2)
        [typeof(Unleash)] = _ =>
        [
            new CalculationBaseVar(6m),
            new ExtraDamageVar(1m).FromOsty(),
            new CalculatedDamageVar(ValueProp.Move).FromOsty()
                .WithMultiplier((CardModel card, Creature _) => CombatStats.SummonDamageBonus(card)),
        ],

        // 守护者：10（升级 15）点伤害，本场战斗中每召唤 2 点 +2 伤害、每消耗 1 张牌 +2 伤害
        [typeof(Protector)] = _ =>
        [
            new CalculationBaseVar(10m),
            new ExtraDamageVar(1m).FromOsty(),
            new CalculatedDamageVar(ValueProp.Move).FromOsty()
                .WithMultiplier((CardModel card, Creature _) => ProtectorBonus(card)),
        ],

        // 剥夺：伤害 10（升级 13）→ 11（升级 14）；灵魂部分保持原版（升级后给灵魂+）
        [typeof(Reave)] = _ => [new DamageVar(11m, ValueProp.Move), new CardsVar(1)],

        // 玷污：伤害 13（升级 17）→ 12（升级 16）；额外转移至多 6（升级 8）层灾厄
        [typeof(Defile)] = _ => [new DamageVar(13m, ValueProp.Move), new PowerVar<DoomPower>(12m)],

        // 荒疫打击：伤害 8（升级 10）→ 10（升级 12）；开局先获得 5 点灾厄（固定值）
        [typeof(BlightStrike)] = _ => [new DamageVar(12m, ValueProp.Move)],

        // 响指：奥斯提伤害 7（升级 10）不变；保留张数 1（升级 2）
        [typeof(Snap)] = _ => [new OstyDamageVar(7m, ValueProp.Move), new CardsVar(1)],

        // 戳击：6（9）→ 7（10），升级仍 +3
        [typeof(Poke)] = _ => [new OstyDamageVar(7m, ValueProp.Move)],

        // 重压：12（16）→ 14（18），升级仍 +4
        [typeof(Flatten)] = _ => [new OstyDamageVar(14m, ValueProp.Move)],

        // 扫荡凝视（哨位模式生成）：改为全体两次 5（8）
        [typeof(SweepingGaze)] = _ => [new OstyDamageVar(5m, ValueProp.Move)],

        // 亡灵精通：召唤 5 → 6，升级改成降费（召唤固定 6）
        [typeof(NecroMastery)] = _ => [new SummonVar(6m)],

        // 根除：11（14）→ 14（17），升级仍 +3
        [typeof(Eradicate)] = _ => [new DamageVar(14m, ValueProp.Move)],

        // 收割：27（33）→ 33（39），升级仍 +6
        [typeof(Reap)] = _ => [new DamageVar(33m, ValueProp.Move)],

        // 击掌：11（13）→ 14（16），升级仍 +2（易伤仍 1 → 2）
        [typeof(HighFive)] = _ => [new OstyDamageVar(14m, ValueProp.Move), new PowerVar<VulnerablePower>(2m)],

        // 猛晃：保留 + 5（7）伤害；本场每累计 15 点奥斯提伤害就多打一次
        [typeof(Rattle)] = _ =>
        [
            new OstyDamageVar(5m, ValueProp.Move),
            new CalculationBaseVar(0m),
            new CalculationExtraVar(1m),
            new CalculatedHitCountVar("CalculatedHits").WithMultiplier(
                (CardModel card, Creature? _) => RattleHits(card))
        ],

        // 播种：8（11）→ 9（12）
        [typeof(Sow)] = _ => [new DamageVar(9m, ValueProp.Move)],

        // 恐惧：7 伤害 / 1 易伤 → 8 / 2（升级仍各 +1 → 9 / 3）
        [typeof(Fear)] = _ => [new DamageVar(8m, ValueProp.Move), new PowerVar<VulnerablePower>(2m)],

        // 埋葬：52（63）→ 59（70）
        [typeof(Bury)] = _ => [new DamageVar(59m, ValueProp.Move)],

        // 死亡行军：基础 8 → 10（升级 +1 → 11），每抽一张的额外伤害 4 → 5（升级 +2 → 7）
        [typeof(DeathMarch)] = _ =>
        [
            new CalculationBaseVar(10m),
            new ExtraDamageVar(5m),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier(
                (CardModel card, Creature? _) => card.IsCanonical
                    ? 0
                    : CombatManager.Instance.History.Entries
                        .OfType<CardDrawnEntry>()
                        .Count((CardDrawnEntry e) =>
                            e.HappenedThisTurn(card.CombatState) && e.Actor == card.Owner.Creature && !e.FromHandDraw))
        ],

        // 吞噬生命：每回合每打出 2 张灵魂，获得「层数」点费用
        // Energy 只用于卡面文案里的能量图标（{Energy:energyIcons()}），数值固定 1，实际给费按能力层数
        [typeof(DevourLife)] = _ => [new PowerVar<DevourLifeReworkPower>(1m), new EnergyVar(1)],

        // 灰烬之灵：每张虚无 5 格挡、回合开始附魔飘荡（数值写死，升级改成降费）
        [typeof(SpiritOfAsh)] = _ => Array.Empty<DynamicVar>(),

        // 鬼火：获得 1 费 → 获得 2 费（消耗、升级给保留都不变）
        [typeof(Wisp)] = _ => [new EnergyVar(2)],

        // 鞭打：给予 13（升级 16）层灾厄、抽 1（升级 2）张
        //       → 给予 14（升级 17）层灾厄、抽 2 张（张数不随升级涨，见 UpgradeReplace）
        [typeof(Scourge)] = _ => [new PowerVar<DoomPower>(15m), new CardsVar(2), new IntVar("Transfer", 10m)],

        // 死亡之门：格挡 6 → 7（升级仍 +1 → 8）
        [typeof(DeathsDoor)] = _ => [new BlockVar(7m, ValueProp.Move), new RepeatVar(2)],

        // 拖延：格挡 11 → 14（升级仍 +2 → 16）
        [typeof(Delay)] = _ => [new BlockVar(14m, ValueProp.Move), new EnergyVar(1)],

        // 湮灭：每次给的灾厄 3 → 5（升级仍 +1 → 6）
        [typeof(Oblivion)] = _ => [new PowerVar<DoomPower>(5m)],

        // 负能量脉冲：格挡 5（升级 6）、全体 7（升级 11）层灾厄
        //             → 格挡 7（升级 8）、全体 9（升级 13）层灾厄
        [typeof(NegativePulse)] = _ => [new BlockVar(9m, ValueProp.Move), new PowerVar<DoomPower>(12m)],

        // 唤起：下回合召唤 2（升级 3）→ 召唤 4（升级 6），费用部分不变
        [typeof(Invoke)] = _ => [new SummonVar(4m), new EnergyVar(2)],

        // 来生：召唤 6（升级 9）→ 召唤 10（升级 13）；免疫层数由奥斯提当前生命值决定（每 14 点一层）
        [typeof(Afterlife)] = _ => [new SummonVar(10m)],

        // 吸引仇恨：召唤 4（升级 5）+ 7（升级 9）格挡
        //           → 召唤 8（升级 10）+ 奥斯提全吸收 1（升级 2）回合
        [typeof(PullAggro)] = _ => [new SummonVar(8m), new IntVar("GuardTurns", 1m)],

        // 刺破帷幕：伤害 10（升级 13）→ 11（升级 14）；「下一张虚无牌耗能 0」由原版能力负责
        [typeof(Veilpiercer)] = _ => [new DamageVar(11m, ValueProp.Move)],

        // 紧追不放：奥斯提伤害 5（升级 6）→ 8（升级 11）；
        //            额外召唤 → 改为「本回合内每攻击该敌人 2 次给 1（升级 2）层易伤」
        [typeof(SicEm)] = _ => [new OstyDamageVar(8m, ValueProp.Move), new PowerVar<RelentlessPower>(1m)],

        // 亡魂牵引：伤害 5（升级 7）→ 4（升级 6），其余数值（按虚无牌张数决定次数）保持原版公式
        [typeof(PullFromBelow)] = _ =>
        [
            new DamageVar(4m, ValueProp.Move),
            new CalculationBaseVar(0m),
            new CalculationExtraVar(1m),
            new CalculatedHitCountVar("CalculatedHits").WithMultiplier((CardModel card, Creature _) =>
                card.IsCanonical
                    ? 0
                    : CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>()
                        .Count(e => e.CardPlay.Player == card.Owner && e.WasEthereal))
        ],

        // 忧郁：格挡 13（升级 17）→ 12（升级 16）；降费条件换成「你或奥斯提获得负面效果」
        [typeof(Melancholy)] = _ => [new BlockVar(12m, ValueProp.Move), new EnergyVar(1)],

        // 领会：抽 3（升级 4）→ 先获得 6 层灾厄，再抽 3（升级 4）
        [typeof(Parse)] = _ => [new CardsVar(3), new PowerVar<DoomPower>(6m)],

        // 挽歌：每次召唤 3（升级 4）→ 5（升级 6），重复 X 次；灵魂张数改为 X+2（Play 里加）
        [typeof(Dirge)] = _ => [new SummonVar(5m)],

        // 增生：额外挂一个「实际能拿多少格挡」的展示变量（含这次治疗 + 本回合已回的血，再吃敏捷/脆弱）
        // 注意：CalculatedBlockVar 读的是 CalculationBase(键 CalculationBase) + CalculationExtra(键 CalculationExtra)，
        //       两个键缺一个都会在 DynamicVarSet.InitializeWithOwner 抛 KeyNotFoundException
        //       （奖励生成时炸 = 打完没有结算界面）。
        [typeof(Spur)] = _ =>
        [
            new CalculationBaseVar(0m),
            new CalculationExtraVar(1m),
            new HealVar(5m),
            new SummonVar(0m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier((CardModel card, Creature? _) => SpurBlockPreview(card)),
        ],

        // 腐烂：每次施加的层数 2（升级 3）→ 1（升级 2），实际施加两次
        [typeof(Putrefy)] = _ => [new DynamicVar("Power", 1m)],

        // 倒数计时：随机敌人灾厄 6（升级 9）→ 4（升级 7）；行为换成自制能力
        [typeof(Countdown)] = _ => [new PowerVar<CountdownReworkPower>(6m)],

        // 血肉戏法：9（升级 13）→ 5（升级 8），再加一个「虚无」
        [typeof(SleightOfFlesh)] = _ => [new PowerVar<SleightOfFleshPower>(5m)],

        // 纠缠：灵魂造成的生命流失 7（升级 9）→ 固定 8；额外生成 1 张灵魂
        [typeof(Haunt)] = _ => [new HpLossVar(8m)],

        // 钙化：奥斯提攻击加成 4（升级 6）→ 3（升级 5）；
        //       并额外记录「每次攻击给你 3（升级 4）格挡」
        [typeof(Calcify)] = _ => [new PowerVar<CalcifyPower>(3m), new BlockVar(3m, ValueProp.Unpowered)],

        // 洁净：召唤 3（升级 5）→ 改成「移除 9（升级 12）层灾厄」，数值用灾厄变量显示，
        //       另外挂一个「实际能拿多少格挡」的展示变量（= min(上限, 你+奥斯提现有的灾厄)）
        [typeof(Cleanse)] = _ =>
        [
            new CalculationBaseVar(0m),
            new CalculationExtraVar(1m),
            new PowerVar<DoomPower>(9m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier((CardModel card, Creature? _) => CleanseBlockPreview(card)),
        ],

        // 能量汲取：伤害 10（升级 12）→ 11（升级 12，升级只 +1）；随机升级 2 张 → 3 张
        [typeof(DrainPower)] = _ => [new DamageVar(11m, ValueProp.Move), new CardsVar(3)],

        // 摧残：易伤/虚弱翻倍持续 2（升级 3）回合 → 3（升级 4）回合，等于「生效回合延长 1」
        [typeof(Debilitate)] = _ => [new DamageVar(10m, ValueProp.Move), new PowerVar<DebilitatePower>(3m)],

        // 厄运之衣：每次施加灾厄时的格挡 3（升级 4）→ 4（升级 5），升级仍 +1
        [typeof(Shroud)] = _ => [new BlockVar(4m, ValueProp.Unpowered)],

        // 违逆：格挡 6（升级 9）→ 9（升级 12）
        [typeof(Defy)] = _ => [new BlockVar(9m, ValueProp.Move), new PowerVar<WeakPower>(1m)],

        // 捕捉灵魂：原版是 3（4）点无法格挡的生命流失 + 3（4）张灵魂，
        //           追加「这名敌人在本回合减少 3（升级 4）点力量」
        [typeof(CaptureSpirit)] = _ =>
        [
            new DamageVar(3m, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move),
            new CardsVar(3),
            new DynamicVar("StrengthLoss", 3m)
        ],

        // 弱化之触：原版只减 8（11）点临时力量，现在自己获得 6 层灾厄，减力量提到 12（升级 16）
        [typeof(EnfeeblingTouch)] = _ => [new DynamicVar("StrengthLoss", 12m), new PowerVar<DoomPower>(6m)],

        // 牺牲：不再「立刻杀死奥斯提」，改为他失去自身 75% 生命值（向下取整），
        // 你获得等量格挡，然后他吸收你的全部灾厄。
        [typeof(Sacrifice)] = _ =>
        [
            new CalculationBaseVar(0m),
            new CalculationExtraVar(1m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier(
                (CardModel card, Creature? _) => SacrificeBlockFrom(card))
        ],

        // 死亡使者：灾厄 21 → 31，虚弱 1 → 2（升级仍 +5 灾厄、+1 虚弱 → 36 灾厄 / 3 虚弱）
        [typeof(Deathbringer)] = _ => [new PowerVar<DoomPower>(31m), new PowerVar<WeakPower>(2m)],

        // 末日降临：灾厄 29 → 35（升级仍 +8 → 43）
        [typeof(EndOfDays)] = _ => [new PowerVar<DoomPower>(35m)],

        // 灵魂风暴：基础伤害提到 12，消耗牌堆里每有一张「灵魂」或「附了飘荡的牌」+6（升级 +7）
        [typeof(SoulStorm)] = _ =>
        [
            new CalculationBaseVar(12m),
            new ExtraDamageVar(6m),
            new CalculatedDamageVar(ValueProp.Move)
                .WithMultiplier((CardModel card, Creature _) => SoulStormFuel(card))
        ],

        // 苦难：改成技能后不再造成伤害，只施加 1 层虚弱（升级 2 层）
        [typeof(Misery)] = _ => [new PowerVar<WeakPower>(1m)],

        // 吊杀：伤害 10 → 11（升级 13）
        [typeof(Hang)] = _ => [new DamageVar(11m, ValueProp.Move)],

        // 精神过载：每回合对自己施加的灾厄 3 → 5（升级仍只加费用）
        [typeof(Neurosurge)] = _ => [new PowerVar<NeurosurgePower>(5m), new EnergyVar(3), new CardsVar(2)],

        // 女妖之嚎：伤害 33 → 36（升级仍只降费）
        [typeof(BansheesCry)] = _ => [new DamageVar(36m, ValueProp.Move), new EnergyVar(2)],

        // 骸骨军团：所有玩家召唤 8（11）
        [typeof(LegionOfBone)] = _ => [new SummonVar(8m)],

        // 死者苏生：召唤 20 → 10（升级 15），并新增「本回合免伤 + 不死于灾厄」
        [typeof(Reanimate)] = _ => [new SummonVar(10m)],
    };

    /// <summary>猛晃：奥斯提每累计这么多伤害，就多打一次（未升级）。</summary>
    private const int HitsPerFifteen = 15;

    /// <summary>猛晃升级后：门槛降到 14。</summary>
    private const int HitsPerFourteen = 14;


    /// <summary>
    /// 守护者的额外伤害：本场战斗中每召唤 2 点 +2，每消耗 1 张牌 +2。
    /// 图鉴里（canonical / 无战斗状态）一律按 0 算。
    /// </summary>
    private static decimal ProtectorBonus(CardModel card)
    {
        try
        {
            if (card.IsCanonical) return 0m;
            if (card.Owner?.PlayerCombatState == null) return 0m;

            decimal summon = CombatStats.TotalSummon(card);
            decimal perSummon = Math.Floor(summon / 2m) * 2m;
            decimal perExhaust = ExhaustCountTracker.TotalFor(card.Owner) * 2m;
            return perSummon + perExhaust;
        }
        catch (Exception e)
        {
            Main.Log.Error($"[{Main.ModId}] 守护者伤害计算失败: {e}");
            return 0m;
        }
    }

    /// <summary>
    /// 猛晃的段数：1 + 本场战斗中奥斯提累计造成的伤害 / 15。
    /// 图鉴等「没有战斗状态」的场合（Owner 没有 PlayerCombatState）一律按 1 段算，
    /// 免得在百科里取不到数据导致界面卡死。
    /// </summary>
    private static int RattleHits(CardModel card)
    {
        try
        {
            // 百科里拿到的是 canonical 模型，没有 Owner，必须提前短路
            if (card.IsCanonical)
            {
                return 1;
            }

            if (card.Owner?.PlayerCombatState == null)
            {
                return 1;
            }

            int total = OstyDamageTracker.TotalFor(card.Owner);
            if (total <= 0)
            {
                return 1;
            }

            // 升级后门槛从 15 降到 14
            int threshold = card.IsUpgraded ? HitsPerFourteen : HitsPerFifteen;
            return 1 + total / threshold;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// 增生能拿到的格挡（给卡面「（获得？点格挡）」提示用）：
    /// 本回合奥斯提已经增加过的生命值 + 这一次治疗的落地量（受生命上限限制）。
    /// 最终数值还会经过 Hook.ModifyBlock，所以敏捷 / 脆弱都会算进去。
    /// </summary>
    private static decimal SpurBlockPreview(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，没有 Owner，必须提前短路
        if (card.IsCanonical)
        {
            return 0m;
        }

        Player? owner = card.Owner;
        if (owner == null)
        {
            return 0m;
        }

        decimal gained = OstyHpGainTracker.GainedThisTurn(owner);

        if (owner.Osty is not { IsAlive: true } osty)
        {
            return gained;
        }

        // 这次治疗最多只能补到满血，溢出的部分不算
        decimal missing = Math.Max(0m, osty.MaxHp - osty.CurrentHp);
        decimal heal = Math.Min(card.DynamicVars.Heal.BaseValue, missing);

        return gained + Math.Max(0m, heal);
    }

    /// <summary>
    /// 牺牲：奥斯提失去的生命值（也就是你获得的格挡）= 它当前生命值的 75%，向下取整。
    /// 取整方向决定它永远杀不死自己（1 血时失去 0 点）。
    /// </summary>
    private static decimal SacrificeBlockFrom(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，没有 Owner，必须提前短路
        if (card.IsCanonical)
        {
            return 0m;
        }

        if (!card.Owner.IsOstyAlive)
        {
            return 0m;
        }

        Creature? osty = card.Owner.Osty;
        if (osty is not { IsAlive: true })
        {
            return 0m;
        }

        return Math.Floor(osty.CurrentHp * 0.75m);
    }

    /// <summary>
    /// 洁净：卡面上「（获得 X 点格挡）」的预览值 = min(移除上限, 你身上的灾厄 + 奥斯提身上的灾厄)。
    /// </summary>
    private static decimal CleanseBlockPreview(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，没有 Owner，必须提前短路
        if (card.IsCanonical)
        {
            return 0m;
        }

        int budget = (int)card.DynamicVars.Doom.BaseValue;
        if (budget <= 0)
        {
            return 0m;
        }

        int available = DoomUtil.PlayerDoom(card);
        if (card.Owner.Osty is { IsAlive: true } osty)
        {
            available += (int)DoomUtil.DoomOf(osty);
        }

        return Math.Min(budget, Math.Max(0, available));
    }

    /// <summary>
    /// 灵魂风暴的加伤来源：消耗牌堆里每一张「灵魂」，或任何附魔了「飘荡」的牌（同一张只算一次）。
    /// </summary>
    private static int SoulStormFuel(CardModel card)
    {
        // 百科里拿到的是 canonical 模型，没有 Owner，必须提前短路
        if (card.IsCanonical)
        {
            return 0;
        }

        var pile = card.Owner?.PlayerCombatState?.ExhaustPile;
        if (pile is null)
        {
            return 0;
        }

        int count = 0;
        foreach (CardModel c in pile.Cards)
        {
            if (c is Soul || c.Enchantment is DriftEnchantment)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 改效果：卡牌类型 -> 新的 OnPlay 实现。
    /// </summary>
    public static readonly Dictionary<Type, Func<CardModel, PlayerChoiceContext, CardPlay, Task>> Play = new()
    {
        // 玷污：造成伤害后，把自己（角色 → 奥斯提）最多 N 层灾厄转给目标。
        // 转移的「扣减」不算施加判定，落到敌人身上只算一次施加。
        [typeof(Defile)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            await DoomUtil.TransferToEnemy(ctx, card, target, card.DynamicVars.Doom.BaseValue);
        },

        // 荒疫打击：先给自己上 4 点灾厄（触发死亡之门 / 厄运之衣），再造成伤害，
        // 最后把「实际造成的伤害」等量转成敌人身上的灾厄。
        [typeof(BlightStrike)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DoomUtil.ApplyToSelf(ctx, card, 4m);

            var attack = await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            int dealt = attack.Results.SelectMany(r => r).Sum(r => r.TotalDamage);
            await PowerCmd.Apply<DoomPower>(ctx, target, dealt, card.Owner.Creature, card);
        },

        // 响指：奥斯提攻击后，从手牌里选 N 张（升级 2 张）还没保留的牌加上保留。
        // prefs 用 (count) 构造 = 至少/至多都是 count，手牌不够时引擎会自动全选。
        [typeof(Snap)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;
            if (Osty.CheckMissingWithAnim(card.Owner)) return;
            if (card.Owner.Osty is not { } osty) return;

            await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue)
                .FromOsty(osty, card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                .Execute(ctx);

            int count = (int)card.DynamicVars.Cards.BaseValue;
            if (count <= 0) return;

            var prefs = new CardSelectorPrefs(new LocString("cards", "SNAP.selectionScreenPrompt"), count);
            var picked = await CardSelectCmd.FromHand(
                ctx, card.Owner, prefs,
                c => !c.Keywords.Contains(CardKeyword.Retain),
                card);

            foreach (var c in picked)
                CardCmd.ApplyKeyword(c, CardKeyword.Retain);
        },

        // 亡魂牵引：先获得 1 层「亡魂牵引」（下一次消耗虚无牌时改为自动打出），
        // 再按「本场战斗打出过的虚无牌数量」连续造成 N 次伤害（N 可为 0，那就没有攻击）。
        [typeof(PullFromBelow)] = async (card, ctx, play) =>
        {
            await PowerCmd.Apply<PullFromBelowPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
            Main.Log.Info($"[{Main.ModId}] 亡魂牵引：已施加，当前层数 = {card.Owner.Creature.GetPower<PullFromBelowPower>()?.Amount.ToString() ?? "无"}");

            if (play.Target is not { } target) return;
            int hits = (int)((CalculatedVar)card.DynamicVars["CalculatedHits"]).Calculate(target);
            if (hits <= 0) return;

            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
                .WithHitCount(hits)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);
        },

        // 鬼火：获得 2 费，然后获得 5 层灾厄（会触发死亡之门 / 厄运之衣）。
        [typeof(Wisp)] = async (card, ctx, play) =>
        {
            await PlayerCmd.GainEnergy(card.DynamicVars.Energy.IntValue, card.Owner);
            await DoomUtil.ApplyToSelf(ctx, card, 5m);
        },

        // 鞭打：先把至多 10（升级 12）层灾厄从自己（角色 → 奥斯提）转给目标，
        // 再给目标上 5（升级 21）层灾厄，最后抽 2 张。
        [typeof(Scourge)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.TransferToEnemy(ctx, card, target, card.DynamicVars["Transfer"].BaseValue);
            await PowerCmd.Apply<DoomPower>(ctx, target, card.DynamicVars.Doom.BaseValue, card.Owner.Creature, card);
            await CardPileCmd.Draw(ctx, card.DynamicVars.Cards.BaseValue, card.Owner);
        },

        // 负能量脉冲：先给自己 6 层灾厄，再起格挡，最后给所有敌人上灾厄。
        [typeof(NegativePulse)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.ApplyToSelf(ctx, card, 4m);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);

            foreach (var enemy in card.CombatState.HittableEnemies)
                await PowerCmd.Apply<DoomPower>(ctx, enemy, card.DynamicVars.Doom.BaseValue, card.Owner.Creature, card);
        },

        // 来生：召唤后按奥斯提的生命值算出免疫层数（每 14 点一层，向下取整）。
        // 召唤本身会给存活的奥斯提 +10 最大生命并回 10 血，这部分也要算进去，
        // 否则刚好卡在 14 的倍数边上时会凭空少一层。
        // 打出时奥斯提已经死了 → 这次召唤只是把它拉起来，不给免疫。
        [typeof(Afterlife)] = async (card, ctx, play) =>
        {
            bool wasAlive = card.Owner.Osty is { IsAlive: true };

            await CreatureCmd.TriggerAnim(card.Owner.Creature,
                Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character),
                Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));

            await OstyCmd.Summon(ctx, card.Owner, card.DynamicVars.Summon.BaseValue, card);

            if (!wasAlive) return;

            decimal wards = card.Owner.Osty is { IsAlive: true } after ? Math.Floor(after.CurrentHp / 14m) : 0m;
            if (wards <= 0m) return;

            await PowerCmd.Apply<DoomWardPower>(ctx, card.Owner.Creature, wards, card.Owner.Creature, card);
        },

        // 守墓人：起格挡，然后把一张灵魂放进抽牌堆；卡牌升级时给的是灵魂+。
        [typeof(GraveWarden)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);

            var souls = Soul.Create(card.Owner, card.DynamicVars.Cards.IntValue, card.CombatState);
            if (card.IsUpgraded)
            {
                foreach (var soul in souls)
                    CardCmd.Upgrade(soul);
            }

            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                souls, PileType.Draw, card.Owner, CardPilePosition.Random));
        },

        // 违逆：先给自己 8 层灾厄，再起格挡，最后给目标虚弱。
        [typeof(Defy)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);
            await PowerCmd.Apply<WeakPower>(ctx, target, card.DynamicVars.Weak.BaseValue, card.Owner.Creature, card);
        },

        // 吸引仇恨：召唤奥斯提，然后让它在 N 回合内替你吃下所有伤害（回到原版全吸收）。
        [typeof(PullAggro)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature,
                Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character),
                Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));

            await OstyCmd.Summon(ctx, card.Owner, card.DynamicVars.Summon.BaseValue, card);

            if (card.Owner.Osty is not { IsAlive: true } osty) return;

            decimal turns = card.DynamicVars["GuardTurns"].BaseValue;

            await PowerCmd.Apply<OstyGuardPower>(ctx, osty, turns, card.Owner.Creature, card);

            // 副作用：吸引仇恨期间，奥斯提每失去 2 点生命值，玩家获得 1 层灾厄
            await PowerCmd.Apply<SpilloverPower>(ctx, card.Owner.Creature, turns, card.Owner.Creature, card);
        },

        // 紧追不放：奥斯提攻击目标，然后在目标身上挂「紧追不放」（本回合内每推进 2 次给易伤）。
        [typeof(SicEm)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            bool hadCounter = target.GetPower<SicEmCounterPower>() != null;

            // 这张牌自己的那一下攻击不算推进，否则一次打出会掉两格。
            RelentlessPower.BeginSuppress();
            try
            {
                if (!Osty.CheckMissingWithAnim(card.Owner) && card.Owner.Osty is { } osty)
                {
                    await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue)
                        .FromOsty(osty, card, play)
                        .Targeting(target)
                        .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                        .Execute(ctx);
                }
            }
            finally
            {
                RelentlessPower.EndSuppress();
            }

            await PowerCmd.Apply<RelentlessPower>(ctx, target,
                card.DynamicVars["RelentlessPower"].BaseValue, card.Owner.Creature, card);

            if (!hadCounter)
            {
                // 第一次上这个状态：计数器从满格开始，这次打出本身不推进。
                await PowerCmd.Apply<SicEmCounterPower>(ctx, target, RelentlessPower.HitsPerTrigger,
                    card.Owner.Creature, card);
                return;
            }

            // 已经挂着：层数叠加之外，这张牌本身也算推进一次（2 → 1）。
            if (target.GetPower<RelentlessPower>() is { } relentless)
            {
                await relentless.Advance(ctx);
            }
        },

        // 领会：先获得 6 层灾厄（会触发死亡之门 / 厄运之衣），再抽牌。
        [typeof(Parse)] = async (card, ctx, play) =>
        {
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
            await CardPileCmd.Draw(ctx, card.DynamicVars.Cards.BaseValue, card.Owner);
        },

        // 挽歌：X 费。召唤 X 次，然后把 X+2 张灵魂（升级后是灵魂+）放进抽牌堆。
        [typeof(Dirge)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            int x = card.ResolveEnergyXValue();
            for (int i = 0; i < x; i++)
            {
                await OstyCmd.Summon(ctx, card.Owner, card.DynamicVars.Summon.BaseValue, card);
            }

            var souls = Soul.Create(card.Owner, x + 2, card.CombatState).ToList();
            if (card.IsUpgraded)
            {
                foreach (var soul in souls)
                    CardCmd.Upgrade(soul);
            }

            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(
                souls, PileType.Draw, card.Owner, CardPilePosition.Random));
        },

        // 增生：治疗奥斯提，然后按「它本回合已经增加的生命值」给你等量格挡（含这次治疗）。
        [typeof(Spur)] = async (card, ctx, play) =>
        {
            if (card.Owner.Osty is not { IsAlive: true } osty) return;

            await CreatureCmd.Heal(osty, card.DynamicVars.Heal.BaseValue);

            decimal gained = OstyHpGainTracker.GainedThisTurn(card.Owner);
            if (gained <= 0m) return;

            await CreatureCmd.GainBlock(card.Owner.Creature, gained, ValueProp.Move, play);
        },

        // 腐烂：虚弱、易伤各施加两次（每次 1（升级 2）层，合计 2（升级 4）层）。
        [typeof(Putrefy)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            decimal amount = card.DynamicVars["Power"].BaseValue;

            for (int i = 0; i < 2; i++)
                await PowerCmd.Apply<WeakPower>(ctx, target, amount, card.Owner.Creature, card);

            for (int i = 0; i < 2; i++)
                await PowerCmd.Apply<VulnerablePower>(ctx, target, amount, card.Owner.Creature, card);
        },

        // 倒数计时：只挂自制能力（只在自己回合触发 + 自己等量消灾厄的逻辑都在能力里）
        [typeof(Countdown)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CountdownReworkPower>(ctx, card.Owner.Creature,
                card.DynamicVars["CountdownReworkPower"].BaseValue, card.Owner.Creature, card);
        },

        // 纠缠：挂原版能力（固定 8 点生命流失）；升级后才额外把 1 张普通灵魂放进手牌
        [typeof(Haunt)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<HauntPower>(ctx, card.Owner.Creature,
                card.DynamicVars.HpLoss.BaseValue, card.Owner.Creature, card);

            if (!card.IsUpgraded) return;

            var souls = Soul.Create(card.Owner, 1, card.CombatState);
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(souls, PileType.Hand, card.Owner));
        },

        // 钙化：挂原版的伤害能力，另外把「每段攻击给多少格挡」记进 CalcifyGuard
        [typeof(Calcify)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CalcifyPower>(ctx, card.Owner.Creature,
                card.DynamicVars["CalcifyPower"].BaseValue, card.Owner.Creature, card);

            CalcifyGuard.Add(card.Owner.Creature, card.DynamicVars.Block.BaseValue);
        },

        // 洁净：移除最多 6（升级 9）层灾厄（先扣骨妹、不够再扣奥斯提），
        //       按实际移除掉的层数获得等量格挡；之后照旧从抽牌堆消耗一张牌。
        [typeof(Cleanse)] = async (card, ctx, play) =>
        {
            int budget = (int)card.DynamicVars.Doom.BaseValue;
            int removed = 0;

            int fromSelf = Math.Min(DoomUtil.PlayerDoom(card), budget);
            if (fromSelf > 0)
            {
                await DoomUtil.RemoveDoom(ctx, card, card.Owner.Creature, fromSelf);
                removed += fromSelf;
            }

            if (removed < budget && card.Owner.Osty is { IsAlive: true } osty)
            {
                int fromOsty = Math.Min(DoomUtil.DoomOf(osty), budget - removed);
                if (fromOsty > 0)
                {
                    await DoomUtil.RemoveDoom(ctx, card, osty, fromOsty);
                    removed += fromOsty;
                }
            }

            if (removed > 0)
            {
                await CreatureCmd.GainBlock(card.Owner.Creature, removed, ValueProp.Move, play);
            }

            // 消耗那一步是可选的：抽牌堆没牌、或者选牌界面出问题时，
            // 已经结算掉的灾厄与格挡不会被撤销（这就是「没能消耗卡牌也能减灾厄」）。
            CardPile drawPile = PileType.Draw.GetPile(card.Owner);
            CardModel? picked = null;
            if (drawPile != null && drawPile.Cards.Count > 0)
            {
                try
                {
                    picked = (await CardSelectCmd.FromCombatPile(ctx, drawPile, card.Owner,
                        new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1))).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Main.Log.Info("洁净：选牌跳过：" + ex.Message);
                }
            }
            else
            {
                Main.Log.Info("洁净：抽牌堆为空，跳过消耗（灾厄与格挡照常结算）");
            }

            if (picked != null)
            {
                await CardCmd.Exhaust(ctx, picked);
            }
        },

        // 捕捉灵魂：3（升级 4）点无法格挡的生命流失 + 把 3（升级 4）张灵魂洗进抽牌堆，
        //           再让这名敌人本回合减少 3（升级 4）点力量（回合结束自动还回去）。
        [typeof(CaptureSpirit)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            await CreatureCmd.Damage(ctx, target, card.DynamicVars.Damage, card, play);

            List<Soul> souls = Soul.Create(card.Owner, card.DynamicVars.Cards.IntValue, card.CombatState).ToList();
            CardCmd.PreviewCardPileAdd(
                await CardPileCmd.AddGeneratedCardsToCombat(souls, PileType.Draw, card.Owner, CardPilePosition.Random));

            await PowerCmd.Apply<CaptureSpiritStrengthDownPower>(ctx, target,
                card.DynamicVars["StrengthLoss"].BaseValue, card.Owner.Creature, card);
        },

        // 弱化之触：先自己吃 6 层灾厄（触发死亡之门 / 厄运之衣），再给对方挂临时减力量
        [typeof(EnfeeblingTouch)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            await DoomUtil.ApplyToSelf(ctx, card, card.DynamicVars.Doom.BaseValue);

            await PowerCmd.Apply<EnfeeblingTouchPower>(ctx, target,
                card.DynamicVars["StrengthLoss"].BaseValue, card.Owner.Creature, card);
        },

        // 吞噬生命：挂上「每 2 张灵魂给费」的能力（重复打出会叠层）
        [typeof(DevourLife)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<DevourLifeReworkPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
        },

        // 灰烬之灵：挂上「虚无牌起防 + 回合开始附魔飘荡」的能力
        [typeof(SpiritOfAsh)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<SpiritOfAshReworkPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
        },

        // 扫荡凝视：对所有敌人造成 5（8）点伤害两次
        [typeof(SweepingGaze)] = async (card, ctx, play) =>
        {
            if (Osty.CheckMissingWithAnim(card.Owner)) return;

            await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue)
                .FromOsty(card.Owner.Osty!, card, play)
                .TargetingAllOpponents(card.CombatState)
                .WithHitCount(2)
                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                .Execute(ctx);
        },

        // 亡灵精通：召唤 6，再挂「敌人吃等量伤害」与「下回合格挡」两个能力
        [typeof(NecroMastery)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);

            await OstyCmd.Summon(ctx, card.Owner, card.DynamicVars.Summon.BaseValue, card);

            await PowerCmd.Apply<NecroMasteryPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
            await PowerCmd.Apply<NecroMasteryGuardPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
        },

        // 碎骨：奥斯提对所有敌人造成伤害 + 你获得格挡 → 然后他损失一半生命值 → 全体 1 层虚弱
        [typeof(BoneShards)] = async (card, ctx, play) =>
        {
            if (Osty.CheckMissingWithAnim(card.Owner)) return;

            Creature osty = card.Owner.Osty!;

            await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue).FromOsty(osty, card, play)
                .TargetingAllOpponents(card.CombatState)
                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                .Execute(ctx);

            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);

            await PowerCmd.Apply<WeakPower>(ctx, card.CombatState.HittableEnemies, 1m,
                card.Owner.Creature, card);

            // 「然后损失一半的生命值」：按当时剩余生命值的一半扣，向上取整
            if (card.Owner.Osty is { IsAlive: true } aliveOsty)
            {
                decimal half = Math.Ceiling(aliveOsty.CurrentHp / 2m);
                if (half > 0m)
                {
                    await CreatureCmd.Damage(ctx, aliveOsty, half,
                        ValueProp.Unblockable | ValueProp.Unpowered, card, play);
                }
            }
        },

        // 能量汲取：伤害 → 升级弃牌堆里的牌（未升级：随机 3 张；升级后：所有可升级的牌）
        [typeof(DrainPower)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            List<CardModel> upgradable = PileType.Discard.GetPile(card.Owner).Cards
                .Where(c => c.IsUpgradable)
                .ToList();

            IEnumerable<CardModel> picks = card.IsUpgraded
                ? upgradable
                : upgradable.TakeRandom(card.DynamicVars.Cards.IntValue, card.Owner.RunState.Rng.CombatCardSelection);

            foreach (CardModel pick in picks.ToList())
            {
                CardCmd.Upgrade(pick);
                CardCmd.Preview(pick);
            }
        },

        // 击掌：原版效果 + 你与奥斯提平分灾厄（奥斯提那一半向上取整）
        [typeof(HighFive)] = async (card, ctx, play) =>
        {
            if (Osty.CheckMissingWithAnim(card.Owner)) return;

            await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue)
                .FromOsty(card.Owner.Osty!, card, play)
                .TargetingAllOpponents(card.CombatState)
                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                .Execute(ctx);

            await PowerCmd.Apply<VulnerablePower>(ctx, card.CombatState.HittableEnemies,
                card.DynamicVars.Vulnerable.BaseValue, card.Owner.Creature, card);

            int doom = DoomUtil.PlayerDoom(card);
            if (doom > 0)
            {
                DoomUtil.TransferToOsty(ctx, card, (doom + 1) / 2);
            }
        },

        // 弥合：格挡 + 至多 3 张手牌本回合保留（其中的虚无牌本回合不自我消耗）
        [typeof(Mend)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);

            var selected = (await CardSelectCmd.FromHand(ctx, card.Owner,
                new CardSelectorPrefs(new LocString("cards", "MEND.selectionScreenPrompt"), 0, Mend.MaxPick),
                null, card)).ToList();

            foreach (CardModel picked in selected)
            {
                picked.GiveSingleTurnRetain();

                if (picked.Keywords.Contains(CardKeyword.Ethereal))
                {
                    MendWard.SuppressEthereal(picked);
                }
            }
        },

        // 哨卫模式：打出后，如果这张牌是升级版，就给这一层能力打标记，
        // 让它每回合生成的是升级版扫荡凝视（见 SentryModePlus）。
        [typeof(SentryMode)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);

            SentryModePower? power = await PowerCmd.Apply<SentryModePower>(ctx, card.Owner.Creature,
                card.DynamicVars["SentryModePower"].BaseValue, card.Owner.Creature, card);

            if (card.IsUpgraded && power != null)
            {
                SentryModePlus.Mark(power);
            }
        },

        // 降灵：先选抽牌堆一张变灵魂，再把最左侧手牌变灵魂
        [typeof(Seance)] = async (card, ctx, play) =>
        {
            Player owner = card.Owner;

            var selection = (await CardSelectCmd.FromCombatPile(ctx, PileType.Draw.GetPile(owner), owner,
                new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1))).ToList();

            await CreatureCmd.TriggerAnim(owner.Creature, "Cast", owner.Character.CastAnimDelay);

            foreach (CardModel picked in selection)
            {
                await CardCmd.TransformTo<Soul>(picked);
            }

            // 选完之后，最右边那张手牌也会变成灵魂
            CardPile hand = PileType.Hand.GetPile(owner);
            CardModel? rightmost = hand.Cards.LastOrDefault();
            if (rightmost != null)
            {
                await CardCmd.TransformTo<Soul>(rightmost);
            }
        },

        // 牺牲：奥斯提失去自身 75% 生命值 → 你获得等量格挡 → 他吸收你全部灾厄
        [typeof(Sacrifice)] = async (card, ctx, play) =>
        {
            if (Osty.CheckMissingWithAnim(card.Owner)) return;

            Creature osty = card.Owner.Osty!;

            decimal blockGain = SacrificeBlockFrom(card);

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            if (blockGain > 0m)
            {
                await CreatureCmd.Damage(ctx, osty, blockGain,
                    ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
            }

            await CreatureCmd.GainBlock(card.Owner.Creature, blockGain, ValueProp.Move, play);

            int doom = DoomUtil.PlayerDoom(card);
            if (doom > 0)
            {
                DoomUtil.TransferToOsty(ctx, card, doom);
            }
        },

        // 死亡使者：先自己吃 13 层灾厄（触发死亡之门 / 厄运之衣），再照原版给全体上灾厄与虚弱
        [typeof(Deathbringer)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            await DoomUtil.ApplyToSelf(ctx, card, 13m);

            if (card.CombatState != null)
            {
                await PowerCmd.Apply<DoomPower>(ctx, card.CombatState.HittableEnemies,
                    card.DynamicVars.Doom.BaseValue, card.Owner.Creature, card);
                await PowerCmd.Apply<WeakPower>(ctx, card.CombatState.HittableEnemies,
                    card.DynamicVars.Weak.BaseValue, card.Owner.Creature, card);
            }
        },

        // 命运同担：原版「你与目标各失去若干力量」不变，额外给一层
        // 「本场战斗中任何敌人获得力量时，奥斯提也获得等量力量」。
        [typeof(SharedFate)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            await PowerCmd.Apply<StrengthPower>(ctx, card.Owner.Creature,
                -card.DynamicVars["PlayerStrengthLoss"].BaseValue, card.Owner.Creature, card);

            await PowerCmd.Apply<StrengthPower>(ctx, target,
                -card.DynamicVars["EnemyStrengthLoss"].BaseValue, card.Owner.Creature, card);

            if (card.Owner.Creature.GetPower<SharedFatePower>() == null)
            {
                await PowerCmd.Apply<SharedFatePower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
            }
        },

        // 虚空之唤：先照旧挂上「回合开始加牌」的能力，再从卡池里随机展示 3 张
        // 「升级前就带虚无」的牌，选中一张置入手牌。
        [typeof(CallOfTheVoid)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CallOfTheVoidPower>(ctx, card.Owner.Creature,
                card.DynamicVars.Cards.BaseValue, card.Owner.Creature, card);

            List<CardModel> ethereal = card.Owner.Character.CardPool
                .GetUnlockedCards(card.Owner.UnlockState, card.Owner.RunState.CardMultiplayerConstraint)
                .Where(c => c.CanonicalKeywords.Contains(CardKeyword.Ethereal))
                .ToList();

            if (ethereal.Count == 0)
            {
                Main.Log.Info($"[{Main.ModId}] 虚空之唤：卡池里没有虚无牌，跳过选择");
                return;
            }

            // 按原版 Quasar/Discovery 的写法：GetDistinctForCombat 负责建牌、设 Owner、去重，
            // 直接 ToMutable() 拿到的牌没有 Owner，选择界面会空着，玩家看不到任何选项。
            List<CardModel> offered = CardFactory
                .GetDistinctForCombat(card.Owner, ethereal, 3, card.Owner.RunState.Rng.CombatCardGeneration)
                .ToList();

            if (offered.Count == 0)
            {
                Main.Log.Info($"[{Main.ModId}] 虚空之唤：抽不出可展示的虚无牌（候选 {ethereal.Count} 张），跳过选择");
                return;
            }

            CardModel? chosen = offered.Count == 1
                ? offered[0]
                : await CardSelectCmd.FromChooseACardScreen(ctx, offered, card.Owner, canSkip: true);
            if (chosen == null) return;

            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(chosen, PileType.Hand, card.Owner));
        },

        // 苦难：先给目标 1 层虚弱，再把它身上的所有负面效果复制给其他敌人。
        [typeof(Misery)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await PowerCmd.Apply<WeakPower>(ctx, target, card.DynamicVars.Weak.BaseValue, card.Owner.Creature, card);

            // 原版的「把目标的减益复制给其他敌人」，去掉了伤害那一段
            var debuffAmounts = (from p in target.Powers
                                 where p.TypeForCurrentAmount == PowerType.Debuff
                                 select (Power: (PowerModel)p.ClonePreservingMutability(), p.Amount))
                .ToDictionary(x => x.Power, x => x.Amount);

            foreach (var kv in debuffAmounts.ToList())
            {
                if (kv.Key is ITemporaryPower temporary)
                {
                    var basePower = debuffAmounts.FirstOrDefault(p => p.Key.Id == temporary.InternallyAppliedPower.Id);
                    if (basePower.Key != null)
                    {
                        debuffAmounts[basePower.Key] += kv.Value;
                    }
                }
            }

            foreach (Creature enemy in card.CombatState.HittableEnemies)
            {
                if (enemy == target) continue;

                foreach (var kv in debuffAmounts)
                {
                    if (kv.Value == 0) continue;

                    PowerModel? existing = PowerCmd.FindExistingInstanceForStacking(kv.Key, enemy, kv.Key.Applier);
                    if (existing != null)
                    {
                        await PowerCmd.ModifyAmount(ctx, existing, kv.Value, kv.Key.Applier, card);
                        continue;
                    }

                    var clone = (PowerModel)kv.Key.ClonePreservingMutability();
                    await PowerCmd.Apply(ctx, clone, enemy, kv.Value, kv.Key.Applier, card);
                }
            }
        },

        // 大限已至：伤害 = 目标的灾厄层数；目标没有灾厄时，改给它 15 层精神过载。
        [typeof(TimesUp)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DamageCmd.Attack(card.DynamicVars.CalculatedDamage)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            if (target.IsAlive && DoomUtil.DoomOf(target) <= 0)
            {
                await PowerCmd.Apply<NeurosurgePower>(ctx, target, 15m, card.Owner.Creature, card);
            }
        },

        // 吊杀：先给 1 层虚弱，再造成伤害并叠「吊杀」翻倍层数。
        [typeof(Hang)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await PowerCmd.Apply<WeakPower>(ctx, target, 1m, card.Owner.Creature, card);

            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            if (!target.IsAlive) return;

            int existing = target.GetPowerAmount<HangPower>();
            int add = Math.Max(2, existing);
            if (existing + add > 999999999)
            {
                add = Math.Max(0, 999999999 - existing);
            }

            await PowerCmd.Apply<HangPower>(ctx, target, add, card.Owner.Creature, card);
        },

        // 幻景：从消耗牌堆里任选任意张「虚无」牌打出（选择顺序即打出顺序）。
        [typeof(Eidolon)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

            CardPile? exhaustPile = PileType.Exhaust.GetPile(card.Owner);
            if (exhaustPile is null || exhaustPile.Cards.Count == 0) return;

            List<CardModel> candidates = exhaustPile.Cards
                .Where(c => c.Keywords.Contains(CardKeyword.Ethereal) && !c.Keywords.Contains(CardKeyword.Unplayable))
                .ToList();
            if (candidates.Count == 0) return;

            IEnumerable<CardModel> picked = await CardSelectCmd.FromCombatPile(
                ctx,
                exhaustPile,
                card.Owner,
                new CardSelectorPrefs(new LocString("cards", "EIDOLON.selectionScreenPrompt"), 0, candidates.Count),
                c => c.Keywords.Contains(CardKeyword.Ethereal) && !c.Keywords.Contains(CardKeyword.Unplayable));

            foreach (CardModel pick in picked.ToList())
            {
                if (pick.Pile?.Type != PileType.Exhaust) continue;
                await CardCmd.AutoPlay(ctx, pick, null);
            }
        },

        // 切断：伤害 13（升级 18）不变，升级后生成的灵魂全部变成「灵魂+」
        [typeof(Severance)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
                .FromCard(card, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(ctx);

            List<Soul> souls = Soul.Create(card.Owner, 3, card.CombatState).ToList();

            if (card.IsUpgraded)
            {
                foreach (Soul soul in souls)
                {
                    soul.UpgradeInternal();
                }
            }

            CardPileAddResult drawResult = await CardPileCmd.AddGeneratedCardToCombat(souls[0], PileType.Draw, card.Owner, CardPilePosition.Random);
            CardPileAddResult discardResult = await CardPileCmd.AddGeneratedCardToCombat(souls[1], PileType.Discard, card.Owner);
            await CardPileCmd.AddGeneratedCardToCombat(souls[2], PileType.Hand, card.Owner);
            CardCmd.PreviewCardPileAdd([drawResult, discardResult]);
        },

        // 骸骨军团：所有玩家召唤 8（11），然后「其他玩家」再获得等同于出牌者奥斯提当前生命值的召唤。
        [typeof(LegionOfBone)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(
                card.Owner.Creature,
                Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character),
                Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));

            // 先快照，避免「召唤抬高了奥斯提血量」把后面的数量算大
            int ostySummon = card.Owner.Osty is { IsAlive: true } osty ? osty.CurrentHp : 0;

            List<Creature> allies = card.CombatState.PlayerCreatures.Where(c => c is { IsAlive: true }).ToList();
            foreach (Creature ally in allies)
            {
                await OstyCmd.Summon(ctx, ally.Player, card.DynamicVars.Summon.BaseValue, card);
            }

            if (ostySummon <= 0) return;

            foreach (Creature ally in allies)
            {
                if (ally == card.Owner.Creature) continue;
                await OstyCmd.Summon(ctx, ally.Player, ostySummon, card);
            }
        },

        // 死者苏生：召唤 5（10），本回合内不受伤害、不死于灾厄。
        [typeof(Reanimate)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(
                card.Owner.Creature,
                Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character),
                Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));

            await OstyCmd.Summon(ctx, card.Owner, card.DynamicVars.Summon.BaseValue, card);

            if (card.Owner.Creature.GetPower<ReanimateWardPower>() == null)
            {
                await PowerCmd.Apply<ReanimateWardPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card);
            }
        },
    };

    /// <summary>
    /// 升级时补的数值（原版 OnUpgrade 只动它自己那几个数值，多出来的在这里加）。
    /// </summary>
    public static readonly Dictionary<Type, Action<CardModel>> Upgrade = new()
    {
        // 玷污：转移层数 6 → 8
        [typeof(Defile)] = card => card.DynamicVars.Doom.UpgradeValueBy(2m),

        // 响指：保留张数 1 → 2
        [typeof(Snap)] = card => card.DynamicVars.Cards.UpgradeValueBy(1m),

        // 唤起：召唤 4 → 6（原版只 +1）
        [typeof(Invoke)] = card => card.DynamicVars.Summon.UpgradeValueBy(1m),

        // 扫荡凝视：升级 5 → 8（原版是 +5）
        [typeof(SweepingGaze)] = card => card.DynamicVars.OstyDamage.UpgradeValueBy(3m),

        // 亡灵精通：升级只降费（2 → 1），召唤固定 6
        [typeof(NecroMastery)] = card => card.EnergyCost.UpgradeBy(-1),

        // 降灵：升级去掉「虚无」（代替原版的降费）
        [typeof(Seance)] = card => card.RemoveKeyword(CardKeyword.Ethereal),

        // 违逆：虚弱 1 → 2（原版只加格挡）
        [typeof(Defy)] = card => card.DynamicVars.Weak.UpgradeValueBy(1m),

        // 死亡使者：虚弱 1 → 2（原版只加灾厄）
        [typeof(Deathbringer)] = card => card.DynamicVars.Weak.UpgradeValueBy(1m),
    };

    /// <summary>
    /// 整段替换原版 OnUpgrade（原版会加我们不想要的数值时用这张表）。
    /// </summary>
    public static readonly Dictionary<Type, Action<CardModel>> UpgradeReplace = new()
    {
        // 吞噬生命：原版升级是「给能力 +1 层」，而能力的数值变量已经被我们换掉，原版读 DevourLifePower 会抛
        // KeyNotFoundException（百科里一预览升级就崩），所以这里整段替换：1 → 0 费。
        [typeof(DevourLife)] = card => card.EnergyCost.UpgradeBy(-1),

        // 灰烬之灵：同上，原版读 BlockOnExhaust 会抛，整段替换成 2 → 1 费。
        [typeof(SpiritOfAsh)] = card => card.EnergyCost.UpgradeBy(-1),

        // 哨卫模式：原版升级是降 1 费，我们现在敲前敲后都 1 费，
        // 升级改成「生成的扫荡凝视是升级版」（见 SentryModePlus），所以这里把原版降费整个取消掉。
        [typeof(SentryMode)] = _ => { },
        // 鞭打：原版升级会「灾厄 +3、抽牌 +1」，我们只要灾厄 15 → 21（+6）、转移 10 → 12（+2），抽牌固定 2 张
        [typeof(Scourge)] = card =>
        {
            card.DynamicVars.Doom.UpgradeValueBy(6m);
            card.DynamicVars["Transfer"].UpgradeValueBy(2m);
        },

        // 吸引仇恨：原版升级会「召唤 +1、格挡 +2」，现在是「召唤 8 → 10、持续 1 → 2 回合」
        [typeof(PullAggro)] = card =>
        {
            card.DynamicVars.Summon.UpgradeValueBy(2m);
            card.DynamicVars["GuardTurns"].UpgradeValueBy(1m);
        },

        // 紧追不放：原版升级「奥斯提伤害 +1、召唤 +1」，现在是「奥斯提伤害 8 → 11、易伤 1 → 2」
        [typeof(SicEm)] = card =>
        {
            card.DynamicVars.OstyDamage.UpgradeValueBy(3m);
            card.DynamicVars["RelentlessPower"].UpgradeValueBy(1m);
        },

        // 增生：原版升级「召唤 +2、治疗 +2」，现在没有召唤了，治疗 5 → 8
        [typeof(Spur)] = card => card.DynamicVars.Heal.UpgradeValueBy(3m),

        // 捕捉灵魂：这张表是「整段替换」，原版的 +1 伤害 / +1 灵魂都不会执行，
        //             所以这里把三个数值一起写满（伤害 3→4、灵魂 3→4、减力量 3→4）
        [typeof(CaptureSpirit)] = card =>
        {
            card.DynamicVars.Damage.UpgradeValueBy(1m);
            card.DynamicVars.Cards.UpgradeValueBy(1);
            card.DynamicVars["StrengthLoss"].UpgradeValueBy(1m);
        },

        // 弱化之触：这张表是「整段替换」，原版的 +3 不会执行，所以这里自己给足 +4（12 → 16）
        [typeof(EnfeeblingTouch)] = card => card.DynamicVars["StrengthLoss"].UpgradeValueBy(4m),

        // 倒数计时：原版升级去取 "CountdownPower" 这个已经不存在的键，这里自己 +3
        [typeof(Countdown)] = card => card.DynamicVars["CountdownReworkPower"].UpgradeValueBy(3m),

        // 血肉戏法：原版 +4，现在 5 → 8 是 +3
        [typeof(SleightOfFlesh)] = card => card.DynamicVars["SleightOfFleshPower"].UpgradeValueBy(3m),

        // 灵魂风暴：原版升级是每张 +2（4 → 6），现在每张 6 → 7 只 +1
        [typeof(SoulStorm)] = card => card.DynamicVars.ExtraDamage.UpgradeValueBy(1m),

        // 纠缠：生命流失固定 8 不涨，升级只把生成的灵魂换成灵魂+
        [typeof(Haunt)] = _ => { },

        // 钙化：原版只 +2 伤害；现在伤害 +2（3 → 5）、格挡 +1（3 → 4）
        [typeof(Calcify)] = card =>
        {
            card.DynamicVars["CalcifyPower"].UpgradeValueBy(2m);
            card.DynamicVars.Block.UpgradeValueBy(1m);
        },

        // 洁净：原版是召唤 3 → 5，现在改成灾厄 9 → 12
        [typeof(Cleanse)] = card => card.DynamicVars.Doom.UpgradeValueBy(3m),

        // 能量汲取：原版升级 +2 伤害，现在 11 → 12 只 +1（张数固定 3，升级改为升级弃牌堆全部牌）
        [typeof(DrainPower)] = card => card.DynamicVars.Damage.UpgradeValueBy(1m),

        // 得力助手：原版升级 +2 伤害，现在 4 → 5 只 +1
        [typeof(RightHandHand)] = card => card.DynamicVars.OstyDamage.UpgradeValueBy(1m),

        // 女妖之嚎：升级不再降费，改成伤害 +8（36 → 44）
        [typeof(BansheesCry)] = card => card.DynamicVars.Damage.UpgradeValueBy(8m),

        // 苦难：虚弱固定 1 层（升级不变），升级只给「保留」
        [typeof(Misery)] = card => card.AddKeyword(CardKeyword.Retain),

        // 大限已至：保留已经常驻，升级改成 2 → 1 费
        [typeof(TimesUp)] = card => card.EnergyCost.UpgradeBy(-1),

        // 吊杀：原版 +3，现在 11 → 13 是 +2
        [typeof(Hang)] = card => card.DynamicVars.Damage.UpgradeValueBy(2m),

        // 骸骨军团：原版 +2，现在 8 → 11 是 +3
        [typeof(LegionOfBone)] = card => card.DynamicVars.Summon.UpgradeValueBy(3m),

        // 死者苏生：原版 +5，现在 5 → 10 仍是 +5
        [typeof(Reanimate)] = card => card.DynamicVars.Summon.UpgradeValueBy(5m),
    };

    /// <summary>
    /// 整段替换关键词（原版的消耗/虚无之类需要加减时用这张表）。
    /// </summary>
    public static readonly Dictionary<Type, CardKeyword[]> Keywords = new()
    {
        // 腐烂：原本只有「消耗」，现在再加「虚无」
        [typeof(Putrefy)] = [CardKeyword.Exhaust, CardKeyword.Ethereal],

        // 血肉戏法：加「虚无」
        [typeof(SleightOfFlesh)] = [CardKeyword.Ethereal],

        // 厄运之衣：加「虚无」
        [typeof(Shroud)] = [CardKeyword.Ethereal],

        // 死亡之门：加「保留」
        [typeof(DeathsDoor)] = [CardKeyword.Retain],

        // 末日降临：加「保留」
        [typeof(EndOfDays)] = [CardKeyword.Retain],

        // 响指：自带保留
        [typeof(Snap)] = [CardKeyword.Retain],

        // 猛晃：加「保留」
        [typeof(Rattle)] = [CardKeyword.Retain],

        [typeof(Undeath)] = [CardKeyword.Ethereal],

        // 大限已至：自带保留
        [typeof(TimesUp)] = [CardKeyword.Retain],

        // 重构：原本只有「消耗」，现在再加「保留」（升级仍会去掉消耗）
        [typeof(Transfigure)] = [CardKeyword.Exhaust, CardKeyword.Retain],

        // 挽歌：取消「消耗」（升级前后都没有）
        [typeof(Dirge)] = [],

        // 幽冥之界（原版多人卡）：额外加「保留」（升级仍会去掉消耗，保留留下）
        [typeof(Underworld)] = [CardKeyword.Exhaust, CardKeyword.Retain],

        // 死者苏生：原本只有「消耗」，现在再加「保留」
        [typeof(Reanimate)] = [CardKeyword.Exhaust, CardKeyword.Retain],
    };

    /// <summary>
    /// 改卡牌类型（原版类型写在构造函数里，这里覆盖 CardModel.Type）。目前只用来把「苦难」从攻击改成技能。
    /// </summary>
    public static readonly Dictionary<Type, CardType> Types = new()
    {
        [typeof(Misery)] = CardType.Skill,
    };

    /// <summary>
    /// 改费用（原版费用写在构造函数里，这里覆盖 CanonicalEnergyCost）。
    /// </summary>
    public static readonly Dictionary<Type, int> Costs = new()
    {
        // 降灵：1 费 → 0 费
        [typeof(Seance)] = 0,

        // 厄运之衣：2 费 → 1 费（升级前后都是 1 费）
        [typeof(Shroud)] = 1,

        // 死者苏生：3 费 → 2 费（升级前后都是 2 费）
        [typeof(Reanimate)] = 2,

        // 哨卫模式：2 费 → 1 费（升级前后都是 1 费，升级改成给升级版扫荡凝视）
        [typeof(SentryMode)] = 1,
    };

    /// <summary>
    /// 整段替换悬停提示（去掉了某个数值时，原版提示会去取不存在的变量，这里直接清空）。
    /// </summary>
    public static readonly Dictionary<Type, Func<CardModel, IEnumerable<IHoverTip>>> Tips = new()
    {
        // 增生：已经没有召唤了，原版的「召唤」悬停提示要去掉
        [typeof(Spur)] = _ => [],

        // 洁净：召唤改成移除灾厄，原版「召唤」悬停提示要去掉，只留「消耗」提示
        [typeof(Cleanse)] = _ => [HoverTipFactory.FromKeyword(CardKeyword.Exhaust)],
    };

    /// <summary>
    /// 这些卡原版自己 override 了 <c>GainsBlock =&gt; true</c>，但重做后已经不提供格挡了。
    /// 不把标记改回 false 的话，「提升格挡值」类附魔的候选列表里还会出现它们（选中了也不会有任何效果）。
    /// </summary>
    public static readonly HashSet<Type> NoGainsBlock = [typeof(PullAggro)];
}
