using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Cards;
using NecroRework.Enchantments;
using NecroRework.Powers;
using NecroRework.Stats;

namespace NecroRework;

internal static class Reworks
{
    // 本文件是「原版卡改造表」：Vars / Play / Upgrade / UpgradeReplace / Keywords / Types / Costs / Tips。
    // 约定：
    //   * Vars  = 整表替换原版的动态变量（同一张卡只能出现一次，重复键会在加载时抛异常）
    //   * Play  = 整段替换 OnPlay（prefix 返回 false，原版实现不再执行）
    //   * Upgrade = 原版 OnUpgrade 执行完之后再追加（原版读的键必须都还在）
    //   * UpgradeReplace = 整段替换 OnUpgrade（数值必须自己写满）
    //   * Keywords / Costs / Types = 整段替换关键词 / 费用 / 卡牌类型
    // 注意：所有在卡面上算数值的 lambda 必须先判 card.IsCanonical 短路，
    //       百科/图鉴里拿到的是 canonical 模型，读 Owner 会抛 CanonicalModelException。
    public static readonly Dictionary<Type, Func<CardModel, IEnumerable<DynamicVar>>> Vars = new Dictionary<Type, Func<CardModel, IEnumerable<DynamicVar>>>
    {
        [typeof(Bodyguard)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(8m) };
        },
        [typeof(Unleash)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(6m),
                (DynamicVar)new ExtraDamageVar(1m).FromOsty(),
                (DynamicVar)((CalculatedVar)new CalculatedDamageVar((ValueProp)8).FromOsty()).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature val) => CombatStats.SummonDamageBonus(card)))
            };
        },
        [typeof(Protector)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(10m),
                (DynamicVar)new ExtraDamageVar(1m).FromOsty(),
                (DynamicVar)((CalculatedVar)new CalculatedDamageVar((ValueProp)8).FromOsty()).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature val) => ProtectorBonus(card)))
            };
        },
        [typeof(Reave)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(11m, (ValueProp)8),
                (DynamicVar)new CardsVar(1)
            };
        },
        [typeof(DanseMacabre)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new PowerVar<DanseMacabrePower>(5m),
                (DynamicVar)new EnergyVar(2)
            };
        },
        [typeof(Fetch)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new OstyDamageVar(4m, (ValueProp)8),
                (DynamicVar)new CardsVar(1)
            };
        },
        [typeof(GlimpseBeyond)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new CardsVar(4) };
        },
        [typeof(TheScythe)] = (CardModel card) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar((decimal)((TheScythe)card).CurrentDamage, (ValueProp)8),
                (DynamicVar)new IntVar("Increase", 6m)
            };
        },
        [typeof(Squeeze)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(25m),
                (DynamicVar)new ExtraDamageVar(6m).FromOsty(),
                (DynamicVar)((CalculatedVar)new CalculatedDamageVar((ValueProp)8).FromOsty()).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature val) => SqueezeCount(card)))
            };
        },
        [typeof(Defile)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(13m, (ValueProp)8),
                (DynamicVar)new PowerVar<DoomPower>(12m)
            };
        },
        [typeof(BlightStrike)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(12m, (ValueProp)8) };
        },
        [typeof(Snap)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new OstyDamageVar(7m, (ValueProp)8),
                (DynamicVar)new CardsVar(1)
            };
        },
        [typeof(Poke)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new OstyDamageVar(7m, (ValueProp)8) };
        },
        [typeof(Flatten)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new OstyDamageVar(14m, (ValueProp)8) };
        },
        [typeof(SweepingGaze)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new OstyDamageVar(5m, (ValueProp)8) };
        },
        [typeof(NecroMastery)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(6m) };
        },
        [typeof(Eradicate)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(14m, (ValueProp)8) };
        },
        [typeof(Reap)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(33m, (ValueProp)8) };
        },
        [typeof(HighFive)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new OstyDamageVar(14m, (ValueProp)8),
                (DynamicVar)new PowerVar<VulnerablePower>(2m)
            };
        },
        [typeof(Rattle)] = (CardModel _) =>
        {
            return new DynamicVar[4]
            {
                (DynamicVar)new OstyDamageVar(5m, (ValueProp)8),
                (DynamicVar)new CalculationBaseVar(0m),
                (DynamicVar)new CalculationExtraVar(1m),
                (DynamicVar)((CalculatedVar)new CalculatedHitCountVar("CalculatedHits")).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature? val) => RattleHits(card)))
            };
        },
        [typeof(Sow)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(9m, (ValueProp)8) };
        },
        [typeof(Fear)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(8m, (ValueProp)8),
                (DynamicVar)new PowerVar<VulnerablePower>(2m)
            };
        },
        [typeof(Bury)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(59m, (ValueProp)8) };
        },
        [typeof(DeathMarch)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(10m),
                (DynamicVar)new ExtraDamageVar(5m),
                (DynamicVar)((CalculatedVar)new CalculatedDamageVar((ValueProp)8)).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature? val) => (!((AbstractModel)card).IsCanonical) ? CombatManager.Instance.History.Entries.OfType<CardDrawnEntry>().Count((CardDrawnEntry e) => ((CombatHistoryEntry)e).HappenedThisTurn(card.CombatState) && ((CombatHistoryEntry)e).Actor == card.Owner.Creature && !e.FromHandDraw) : 0))
            };
        },
        [typeof(DevourLife)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new PowerVar<DevourLifeReworkPower>(1m),
                (DynamicVar)new EnergyVar(1)
            };
        },
        [typeof(SpiritOfAsh)] = (CardModel _) => Array.Empty<DynamicVar>(),
        [typeof(Wisp)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new EnergyVar(2) };
        },
        [typeof(Scourge)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new PowerVar<DoomPower>(15m),
                (DynamicVar)new CardsVar(2),
                (DynamicVar)new IntVar("Transfer", 10m)
            };
        },
        [typeof(DeathsDoor)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new BlockVar(7m, (ValueProp)8),
                (DynamicVar)new RepeatVar(2)
            };
        },
        [typeof(Delay)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new BlockVar(14m, (ValueProp)8),
                (DynamicVar)new EnergyVar(1)
            };
        },
        [typeof(Oblivion)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)(object)new PowerVar<DoomPower>(5m) },
        [typeof(NegativePulse)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new BlockVar(9m, (ValueProp)8),
                (DynamicVar)new PowerVar<DoomPower>(12m)
            };
        },
        [typeof(Invoke)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new SummonVar(4m),
                (DynamicVar)new EnergyVar(2)
            };
        },
        [typeof(Afterlife)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(10m) };
        },
        [typeof(PullAggro)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new SummonVar(8m),
                (DynamicVar)new IntVar("GuardTurns", 1m)
            };
        },
        [typeof(Veilpiercer)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(11m, (ValueProp)8) };
        },
        [typeof(SicEm)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new OstyDamageVar(8m, (ValueProp)8),
                (DynamicVar)new PowerVar<RelentlessPower>(1m)
            };
        },
        [typeof(PullFromBelow)] = (CardModel _) =>
        {
            return new DynamicVar[4]
            {
                (DynamicVar)new DamageVar(4m, (ValueProp)8),
                (DynamicVar)new CalculationBaseVar(0m),
                (DynamicVar)new CalculationExtraVar(1m),
                (DynamicVar)((CalculatedVar)new CalculatedHitCountVar("CalculatedHits")).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature val) => (!((AbstractModel)card).IsCanonical) ? CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>().Count((CardPlayFinishedEntry e) => e.CardPlay.Player == card.Owner && e.WasEthereal) : 0))
            };
        },
        [typeof(Melancholy)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new BlockVar(12m, (ValueProp)8),
                (DynamicVar)new EnergyVar(1)
            };
        },
        [typeof(Parse)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new CardsVar(3),
                (DynamicVar)new PowerVar<DoomPower>(6m)
            };
        },
        [typeof(Dirge)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(5m) };
        },
        [typeof(Spur)] = (CardModel _) =>
        {
            return new DynamicVar[5]
            {
                (DynamicVar)new CalculationBaseVar(0m),
                (DynamicVar)new CalculationExtraVar(1m),
                (DynamicVar)new HealVar(5m),
                (DynamicVar)new SummonVar(0m),
                (DynamicVar)((CalculatedVar)new CalculatedBlockVar((ValueProp)8)).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature? val) => SpurBlockPreview(card)))
            };
        },
        [typeof(Putrefy)] = (CardModel _) =>
        {
            return new DynamicVar[1] { new DynamicVar("Power", 1m) };
        },
        [typeof(Countdown)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)(object)new PowerVar<CountdownReworkPower>(6m) },
        // 血肉戏法：5 → 8（升级 +3 → 11）
        [typeof(SleightOfFlesh)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)(object)new PowerVar<SleightOfFleshPower>(8m) },
        // 得力助手：4 → 5（升级 +2 → 7）
        [typeof(RightHandHand)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)new OstyDamageVar(5m, ValueProp.Move) },
        [typeof(Haunt)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new HpLossVar(8m) };
        },
        [typeof(Calcify)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new PowerVar<CalcifyPower>(3m),
                (DynamicVar)new BlockVar(3m, (ValueProp)4)
            };
        },
        [typeof(Cleanse)] = (CardModel _) =>
        {
            return new DynamicVar[4]
            {
                (DynamicVar)new CalculationBaseVar(0m),
                (DynamicVar)new CalculationExtraVar(1m),
                (DynamicVar)new PowerVar<DoomPower>(9m),
                (DynamicVar)((CalculatedVar)new CalculatedBlockVar((ValueProp)8)).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature? val) => CleanseBlockPreview(card)))
            };
        },
        [typeof(DrainPower)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(11m, (ValueProp)8),
                (DynamicVar)new CardsVar(3)
            };
        },
        [typeof(Debilitate)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(10m, (ValueProp)8),
                (DynamicVar)new PowerVar<DebilitatePower>(3m)
            };
        },
        [typeof(Shroud)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new BlockVar(4m, (ValueProp)4) };
        },
        [typeof(Defy)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new BlockVar(9m, (ValueProp)8),
                (DynamicVar)new PowerVar<WeakPower>(1m)
            };
        },
        [typeof(CaptureSpirit)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new DamageVar(3m, (ValueProp)14),
                (DynamicVar)new CardsVar(3),
                new DynamicVar("StrengthLoss", 3m)
            };
        },
        [typeof(EnfeeblingTouch)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                new DynamicVar("StrengthLoss", 12m),
                (DynamicVar)new PowerVar<DoomPower>(6m)
            };
        },
        [typeof(Sacrifice)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(0m),
                (DynamicVar)new CalculationExtraVar(1m),
                (DynamicVar)((CalculatedVar)new CalculatedBlockVar((ValueProp)8)).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature? val) => SacrificeBlockFrom(card)))
            };
        },
        [typeof(Deathbringer)] = (CardModel _) => new DynamicVar[2]
        {
            (DynamicVar)new PowerVar<DoomPower>(31m),
            (DynamicVar)new PowerVar<WeakPower>(2m)
        },
        [typeof(EndOfDays)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)(object)new PowerVar<DoomPower>(35m) },
        [typeof(SoulStorm)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new CalculationBaseVar(12m),
                (DynamicVar)new ExtraDamageVar(6m),
                (DynamicVar)((CalculatedVar)new CalculatedDamageVar((ValueProp)8)).WithMultiplier((Func<CardModel, Creature, decimal>)((CardModel card, Creature val) => SoulStormFuel(card)))
            };
        },
        [typeof(Misery)] = (CardModel _) => new DynamicVar[1] { (DynamicVar)(object)new PowerVar<WeakPower>(1m) },
        [typeof(Hang)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new DamageVar(11m, (ValueProp)8) };
        },
        [typeof(Neurosurge)] = (CardModel _) =>
        {
            return new DynamicVar[3]
            {
                (DynamicVar)new PowerVar<NeurosurgePower>(5m),
                (DynamicVar)new EnergyVar(3),
                (DynamicVar)new CardsVar(2)
            };
        },
        [typeof(BansheesCry)] = (CardModel _) =>
        {
            return new DynamicVar[2]
            {
                (DynamicVar)new DamageVar(36m, (ValueProp)8),
                (DynamicVar)new EnergyVar(2)
            };
        },
        [typeof(LegionOfBone)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(8m) };
        },
        [typeof(Reanimate)] = (CardModel _) =>
        {
            return new DynamicVar[1] { (DynamicVar)new SummonVar(10m) };
        }
    };

    private const int HitsPerFifteen = 15;

    private const int HitsPerFourteen = 14;

    public static readonly Dictionary<Type, Func<CardModel, PlayerChoiceContext, CardPlay, Task>> Play = new Dictionary<Type, Func<CardModel, PlayerChoiceContext, CardPlay, Task>>
    {
        [typeof(Defile)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).FromCard(card, play).Targeting(target)
                    .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                    .Execute(ctx);
                await DoomUtil.TransferToEnemy(ctx, card, target, ((DynamicVar)card.DynamicVars.Doom).BaseValue);
            }
        },
        [typeof(BlightStrike)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await DoomUtil.ApplyToSelf(ctx, card, 4m);
                int num = (await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).FromCard(card, play).Targeting(target)
                    .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                    .Execute(ctx)).Results.SelectMany((List<DamageResult> r) => r).Sum((DamageResult r) => r.TotalDamage);
                await PowerCmd.Apply<DoomPower>(ctx, target, (decimal)num, card.Owner.Creature, card, false);
            }
        },
        [typeof(Snap)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null && !Osty.CheckMissingWithAnim(card.Owner))
            {
                Creature osty = card.Owner.Osty;
                if (osty != null)
                {
                    await DamageCmd.Attack(((DynamicVar)card.DynamicVars.OstyDamage).BaseValue).FromOsty(osty, card, play).Targeting(target)
                        .WithHitFx("vfx/vfx_attack_blunt", (string)null, "blunt_attack.mp3")
                        .Execute(ctx);
                    int num = (int)((DynamicVar)card.DynamicVars.Cards).BaseValue;
                    if (num > 0)
                    {
                        CardSelectorPrefs val = new CardSelectorPrefs(new LocString("cards", "SNAP.selectionScreenPrompt"), num);
                        foreach (CardModel item in await CardSelectCmd.FromHand(ctx, card.Owner, val, (Func<CardModel, bool>)((CardModel c) => !c.Keywords.Contains((CardKeyword)5)), (AbstractModel)(object)card))
                        {
                            CardCmd.ApplyKeyword(item, new CardKeyword[1] { (CardKeyword)5 });
                        }
                    }
                }
            }
        },
        [typeof(PullFromBelow)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await PowerCmd.Apply<PullFromBelowPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
            Logger log = Main.Log;
            PullFromBelowPower power = card.Owner.Creature.GetPower<PullFromBelowPower>();
            log.Info("[NecroRework] 亡魂牵引：已施加，当前层数 = " + (((power != null) ? ((PowerModel)power).Amount.ToString() : null) ?? "无"), 1);
            Creature target = play.Target;
            if (target != null)
            {
                int num = (int)((CalculatedVar)card.DynamicVars["CalculatedHits"]).Calculate(target);
                if (num > 0)
                {
                    await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).WithHitCount(num).FromCard(card, play)
                        .Targeting(target)
                        .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                        .Execute(ctx);
                }
            }
        },
        [typeof(Wisp)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await PlayerCmd.GainEnergy((decimal)((DynamicVar)card.DynamicVars.Energy).IntValue, card.Owner);
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
        },
        [typeof(Scourge)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await DoomUtil.TransferToEnemy(ctx, card, target, card.DynamicVars["Transfer"].BaseValue);
                await PowerCmd.Apply<DoomPower>(ctx, target, ((DynamicVar)card.DynamicVars.Doom).BaseValue, card.Owner.Creature, card, false);
                await CardPileCmd.Draw(ctx, ((DynamicVar)card.DynamicVars.Cards).BaseValue, card.Owner, false);
            }
        },
        [typeof(NegativePulse)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.ApplyToSelf(ctx, card, 4m);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play, false);
            foreach (Creature hittableEnemy in card.CombatState.HittableEnemies)
            {
                await PowerCmd.Apply<DoomPower>(ctx, hittableEnemy, ((DynamicVar)card.DynamicVars.Doom).BaseValue, card.Owner.Creature, card, false);
            }
        },
        [typeof(Afterlife)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature osty = card.Owner.Osty;
            bool wasAlive = osty != null && osty.IsAlive;
            await CreatureCmd.TriggerAnim(card.Owner.Creature, Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character), Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));
            await OstyCmd.Summon(ctx, card.Owner, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            if (wasAlive)
            {
                Creature osty2 = card.Owner.Osty;
                decimal num = ((osty2 != null && osty2.IsAlive) ? Math.Floor((decimal)osty2.CurrentHp / 14m) : 0m);
                if (!(num <= 0m))
                {
                    await PowerCmd.Apply<DoomWardPower>(ctx, card.Owner.Creature, num, card.Owner.Creature, card, false);
                }
            }
        },
        [typeof(GraveWarden)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play, false);
            IEnumerable<Soul> enumerable = Soul.Create(card.Owner, ((DynamicVar)card.DynamicVars.Cards).IntValue, card.CombatState);
            if (card.IsUpgraded)
            {
                foreach (Soul item2 in enumerable)
                {
                    CardCmd.Upgrade((CardModel)(object)item2, (CardPreviewStyle)1);
                }
            }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)enumerable, (PileType)1, card.Owner, (CardPilePosition)3), 1.2f, (CardPreviewStyle)1);
        },
        [typeof(Defy)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await DoomUtil.ApplyToSelf(ctx, card, 7m);
                await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play, false);
                await PowerCmd.Apply<WeakPower>(ctx, target, ((DynamicVar)card.DynamicVars.Weak).BaseValue, card.Owner.Creature, card, false);
            }
        },
        [typeof(PullAggro)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character), Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));
            await OstyCmd.Summon(ctx, card.Owner, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            Creature osty = card.Owner.Osty;
            if (osty != null && osty.IsAlive)
            {
                decimal turns = card.DynamicVars["GuardTurns"].BaseValue;
                await PowerCmd.Apply<OstyGuardPower>(ctx, osty, turns, card.Owner.Creature, card, false);
                await PowerCmd.Apply<SpilloverPower>(ctx, card.Owner.Creature, turns, card.Owner.Creature, card, false);
            }
        },
        [typeof(SicEm)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                bool hadCounter = target.GetPower<SicEmCounterPower>() != null;
                RelentlessPower.BeginSuppress();
                try
                {
                    if (!Osty.CheckMissingWithAnim(card.Owner))
                    {
                        Creature osty = card.Owner.Osty;
                        if (osty != null)
                        {
                            await DamageCmd.Attack(((DynamicVar)card.DynamicVars.OstyDamage).BaseValue).FromOsty(osty, card, play).Targeting(target)
                                .WithHitFx("vfx/vfx_attack_blunt", (string)null, "blunt_attack.mp3")
                                .Execute(ctx);
                        }
                    }
                }
                finally
                {
                    RelentlessPower.EndSuppress();
                }
                await PowerCmd.Apply<RelentlessPower>(ctx, target, card.DynamicVars["RelentlessPower"].BaseValue, card.Owner.Creature, card, false);
                if (!hadCounter)
                {
                    await PowerCmd.Apply<SicEmCounterPower>(ctx, target, 2m, card.Owner.Creature, card, false);
                }
                else
                {
                    RelentlessPower power = target.GetPower<RelentlessPower>();
                    if (power != null)
                    {
                        await power.Advance(ctx);
                    }
                }
            }
        },
        [typeof(Parse)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
            await CardPileCmd.Draw(ctx, ((DynamicVar)card.DynamicVars.Cards).BaseValue, card.Owner, false);
        },
        [typeof(Dirge)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            int x = card.ResolveEnergyXValue();
            for (int i = 0; i < x; i++)
            {
                await OstyCmd.Summon(ctx, card.Owner, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            }
            List<Soul> list = Soul.Create(card.Owner, x + 2, card.CombatState).ToList();
            if (card.IsUpgraded)
            {
                foreach (Soul item3 in list)
                {
                    CardCmd.Upgrade((CardModel)(object)item3, (CardPreviewStyle)1);
                }
            }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)list, (PileType)1, card.Owner, (CardPilePosition)3), 1.2f, (CardPreviewStyle)1);
        },
        [typeof(Spur)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature osty = card.Owner.Osty;
            if (osty != null && osty.IsAlive)
            {
                await CreatureCmd.Heal(osty, ((DynamicVar)card.DynamicVars.Heal).BaseValue, true);
                decimal num = OstyHpGainTracker.GainedThisTurn(card.Owner);
                if (!(num <= 0m))
                {
                    await CreatureCmd.GainBlock(card.Owner.Creature, num, (ValueProp)8, play, false);
                }
            }
        },
        [typeof(Putrefy)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                decimal amount = card.DynamicVars["Power"].BaseValue;
                for (int i = 0; i < 2; i++)
                {
                    await PowerCmd.Apply<WeakPower>(ctx, target, amount, card.Owner.Creature, card, false);
                }
                for (int i = 0; i < 2; i++)
                {
                    await PowerCmd.Apply<VulnerablePower>(ctx, target, amount, card.Owner.Creature, card, false);
                }
            }
        },
        [typeof(Countdown)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CountdownReworkPower>(ctx, card.Owner.Creature, card.DynamicVars["CountdownReworkPower"].BaseValue, card.Owner.Creature, card, false);
        },
        [typeof(Haunt)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<HauntPower>(ctx, card.Owner.Creature, ((DynamicVar)card.DynamicVars.HpLoss).BaseValue, card.Owner.Creature, card, false);
            if (card.IsUpgraded)
            {
                CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)Soul.Create(card.Owner, 1, card.CombatState), (PileType)2, card.Owner, (CardPilePosition)1), 1.2f, (CardPreviewStyle)1);
            }
        },
        [typeof(Calcify)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CalcifyPower>(ctx, card.Owner.Creature, card.DynamicVars["CalcifyPower"].BaseValue, card.Owner.Creature, card, false);
            CalcifyGuard.Add(card.Owner.Creature, ((DynamicVar)card.DynamicVars.Block).BaseValue);
        },
        [typeof(Cleanse)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            int budget = (int)((DynamicVar)card.DynamicVars.Doom).BaseValue;
            int removed = 0;
            int fromSelf = Math.Min(DoomUtil.PlayerDoom(card), budget);
            if (fromSelf > 0)
            {
                await DoomUtil.RemoveDoom(ctx, card, card.Owner.Creature, fromSelf);
                removed += fromSelf;
            }
            if (removed < budget)
            {
                Creature osty = card.Owner.Osty;
                if (osty != null && osty.IsAlive)
                {
                    int fromOsty = Math.Min(DoomUtil.DoomOf(osty), budget - removed);
                    if (fromOsty > 0)
                    {
                        await DoomUtil.RemoveDoom(ctx, card, osty, fromOsty);
                        removed += fromOsty;
                    }
                }
            }
            if (removed > 0)
            {
                await CreatureCmd.GainBlock(card.Owner.Creature, (decimal)removed, (ValueProp)8, play, false);
            }
            CardPile pile = PileTypeExtensions.GetPile((PileType)1, card.Owner);
            CardModel picked = null;
            if (pile != null && pile.Cards.Count > 0)
            {
                try
                {
                    picked = (await CardSelectCmd.FromCombatPile(ctx, pile, card.Owner, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1))).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Main.Log.Info("洁净：选牌跳过：" + ex.Message, 1);
                }
            }
            else
            {
                Main.Log.Info("洁净：抽牌堆为空，跳过消耗（灾厄与格挡照常结算）", 1);
            }
            if (picked != null)
            {
                await CardCmd.Exhaust(ctx, picked, false, false);
            }
        },
        [typeof(CaptureSpirit)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await CreatureCmd.Damage(ctx, target, card.DynamicVars.Damage, card, play);
                CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)Soul.Create(card.Owner, ((DynamicVar)card.DynamicVars.Cards).IntValue, card.CombatState).ToList(), (PileType)1, card.Owner, (CardPilePosition)3), 1.2f, (CardPreviewStyle)1);
                await PowerCmd.Apply<CaptureSpiritStrengthDownPower>(ctx, target, card.DynamicVars["StrengthLoss"].BaseValue, card.Owner.Creature, card, false);
            }
        },
        [typeof(EnfeeblingTouch)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await DoomUtil.ApplyToSelf(ctx, card, ((DynamicVar)card.DynamicVars.Doom).BaseValue);
                await PowerCmd.Apply<EnfeeblingTouchPower>(ctx, target, card.DynamicVars["StrengthLoss"].BaseValue, card.Owner.Creature, card, false);
            }
        },
        [typeof(DevourLife)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<DevourLifeReworkPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
        },
        [typeof(SpiritOfAsh)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<SpiritOfAshReworkPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
        },
        // 扫荡凝视：对所有敌人造成 5（9）点伤害两次。
        // 段数交给引擎的 Hook.ModifyAttackHitCount 处理（带「拇指夹」时会变 3 段），
        // 卡面文字由 src\SweepingGazeHits.cs 同步成 2 次 / 3 次。
        [typeof(SweepingGaze)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            if (Osty.CheckMissingWithAnim(card.Owner)) return;

            var command = DamageCmd.Attack(((DynamicVar)card.DynamicVars.OstyDamage).BaseValue)
                .FromOsty(card.Owner.Osty, card, play)
                .TargetingAllOpponents(card.CombatState)
                .WithHitCount(2)
                .WithHitFx("vfx/vfx_attack_blunt", (string)null, "blunt_attack.mp3");

            await command.Execute(ctx);
            Main.Log.Info($"[{Main.ModId}] 扫荡凝视：实际打出了 {command.Results?.Count() ?? 0} 段");
        },
        [typeof(NecroMastery)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await OstyCmd.Summon(ctx, card.Owner, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            await PowerCmd.Apply<NecroMasteryPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
            await PowerCmd.Apply<NecroMasteryGuardPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
        },
        [typeof(BoneShards)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            if (!Osty.CheckMissingWithAnim(card.Owner))
            {
                Creature osty = card.Owner.Osty;
                await DamageCmd.Attack(((DynamicVar)card.DynamicVars.OstyDamage).BaseValue).FromOsty(osty, card, play).TargetingAllOpponents(card.CombatState)
                    .WithHitFx("vfx/vfx_attack_blunt", (string)null, "blunt_attack.mp3")
                    .Execute(ctx);
                await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play, false);
                await PowerCmd.Apply<WeakPower>(ctx, (IEnumerable<Creature>)card.CombatState.HittableEnemies, 1m, card.Owner.Creature, card, false);
                Creature osty2 = card.Owner.Osty;
                if (osty2 != null && osty2.IsAlive)
                {
                    decimal num = Math.Ceiling((decimal)osty2.CurrentHp / 2m);
                    if (num > 0m)
                    {
                        await CreatureCmd.Damage(ctx, osty2, num, (ValueProp)6, card, play);
                    }
                }
            }
        },
        [typeof(DrainPower)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target == null)
            {
                return;
            }
            await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).FromCard(card, play).Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                .Execute(ctx);
            List<CardModel> list = PileTypeExtensions.GetPile((PileType)3, card.Owner).Cards.Where((CardModel c) => c.IsUpgradable).ToList();
            IEnumerable<CardModel> source;
            if (!card.IsUpgraded)
            {
                source = IEnumerableExtensions.TakeRandom<CardModel>((IEnumerable<CardModel>)list, ((DynamicVar)card.DynamicVars.Cards).IntValue, card.Owner.RunState.Rng.CombatCardSelection);
            }
            else
            {
                IEnumerable<CardModel> enumerable = list;
                source = enumerable;
            }
            foreach (CardModel item4 in source.ToList())
            {
                CardCmd.Upgrade(item4, (CardPreviewStyle)1);
                CardCmd.Preview(item4, 1.2f, (CardPreviewStyle)1);
            }
        },
        [typeof(GlimpseBeyond)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            List<Creature> list = (from c in card.CombatState.GetTeammatesOf(card.Owner.Creature)
                where c != null && c.IsAlive && c.IsPlayer
                select c).ToList();
            foreach (Creature creature in list)
            {
                Player player = creature.Player;
                if (player != null)
                {
                    List<Soul> list2 = Soul.Create(player, ((DynamicVar)card.DynamicVars.Cards).IntValue, card.CombatState).ToList();
                    foreach (Soul item5 in list2)
                    {
                        SoulEnchantmentRoll.ApplyRandom((CardModel)(object)item5, card.Owner);
                    }
                    IReadOnlyList<CardPileAddResult> readOnlyList = await CardPileCmd.AddGeneratedCardsToCombat((IEnumerable<CardModel>)list2, (PileType)1, card.Owner, (CardPilePosition)3);
                    if (LocalContext.IsMe(creature))
                    {
                        CardCmd.PreviewCardPileAdd(readOnlyList, 1.2f, (CardPreviewStyle)1);
                    }
                }
            }
        },
        [typeof(HighFive)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            if (!Osty.CheckMissingWithAnim(card.Owner))
            {
                await DamageCmd.Attack(((DynamicVar)card.DynamicVars.OstyDamage).BaseValue).FromOsty(card.Owner.Osty, card, play).TargetingAllOpponents(card.CombatState)
                    .WithHitFx("vfx/vfx_attack_blunt", (string)null, "blunt_attack.mp3")
                    .Execute(ctx);
                await PowerCmd.Apply<VulnerablePower>(ctx, (IEnumerable<Creature>)card.CombatState.HittableEnemies, ((DynamicVar)card.DynamicVars.Vulnerable).BaseValue, card.Owner.Creature, card, false);
                int num = DoomUtil.PlayerDoom(card);
                if (num > 0)
                {
                    DoomUtil.TransferToOsty(ctx, card, (num + 1) / 2);
                }
            }
        },
        [typeof(Mend)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play, false);
            foreach (CardModel item6 in (await CardSelectCmd.FromHand(ctx, card.Owner, new CardSelectorPrefs(new LocString("cards", "MEND.selectionScreenPrompt"), 0, 3), (Func<CardModel, bool>)null, (AbstractModel)(object)card)).ToList())
            {
                item6.GiveSingleTurnRetain();
                if (item6.Keywords.Contains((CardKeyword)2))
                {
                    MendWard.SuppressEthereal(item6);
                }
            }
        },
        [typeof(SentryMode)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            SentryModePower val = await PowerCmd.Apply<SentryModePower>(ctx, card.Owner.Creature, card.DynamicVars["SentryModePower"].BaseValue, card.Owner.Creature, card, false);
            if (card.IsUpgraded && val != null)
            {
                SentryModePlus.Mark((PowerModel)(object)val);
            }
        },
        [typeof(Seance)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Player owner = card.Owner;
            List<CardModel> selection = (await CardSelectCmd.FromCombatPile(ctx, PileTypeExtensions.GetPile((PileType)1, owner), owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1))).ToList();
            await CreatureCmd.TriggerAnim(owner.Creature, "Cast", owner.Character.CastAnimDelay);
            foreach (CardModel item7 in selection)
            {
                await CardCmd.TransformTo<Soul>(item7, (CardPreviewStyle)1);
            }
            CardModel val = PileTypeExtensions.GetPile((PileType)2, owner).Cards.LastOrDefault();
            if (val != null)
            {
                await CardCmd.TransformTo<Soul>(val, (CardPreviewStyle)1);
            }
        },
        [typeof(Sacrifice)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            if (!Osty.CheckMissingWithAnim(card.Owner))
            {
                Creature osty = card.Owner.Osty;
                decimal blockGain = SacrificeBlockFrom(card);
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                if (blockGain > 0m)
                {
                    await CreatureCmd.Damage(ctx, osty, blockGain, (ValueProp)14, card, play);
                }
                await CreatureCmd.GainBlock(card.Owner.Creature, blockGain, (ValueProp)8, play, false);
                int num = DoomUtil.PlayerDoom(card);
                if (num > 0)
                {
                    DoomUtil.TransferToOsty(ctx, card, num);
                }
            }
        },
        [typeof(Deathbringer)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.ApplyToSelf(ctx, card, 13m);
            if (card.CombatState != null)
            {
                await PowerCmd.Apply<DoomPower>(ctx, (IEnumerable<Creature>)card.CombatState.HittableEnemies, ((DynamicVar)card.DynamicVars.Doom).BaseValue, card.Owner.Creature, card, false);
                await PowerCmd.Apply<WeakPower>(ctx, (IEnumerable<Creature>)card.CombatState.HittableEnemies, ((DynamicVar)card.DynamicVars.Weak).BaseValue, card.Owner.Creature, card, false);
            }
        },
        [typeof(SharedFate)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await PowerCmd.Apply<StrengthPower>(ctx, card.Owner.Creature, -card.DynamicVars["PlayerStrengthLoss"].BaseValue, card.Owner.Creature, card, false);
                await PowerCmd.Apply<StrengthPower>(ctx, target, -card.DynamicVars["EnemyStrengthLoss"].BaseValue, card.Owner.Creature, card, false);
                if (card.Owner.Creature.GetPower<SharedFatePower>() == null)
                {
                    await PowerCmd.Apply<SharedFatePower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
                }
            }
        },
        [typeof(CallOfTheVoid)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "PowerUp", card.Owner.Character.PowerUpAnimDelay);
            await PowerCmd.Apply<CallOfTheVoidPower>(ctx, card.Owner.Creature, ((DynamicVar)card.DynamicVars.Cards).BaseValue, card.Owner.Creature, card, false);
            List<CardModel> list = (from c in card.Owner.Character.CardPool.GetUnlockedCards(card.Owner.UnlockState, card.Owner.RunState.CardMultiplayerConstraint)
                where c.CanonicalKeywords.Contains((CardKeyword)2)
                select c).ToList();
            if (list.Count == 0)
            {
                Main.Log.Info("[NecroRework] 虚空之唤：卡池里没有虚无牌，跳过选择", 1);
            }
            else
            {
                List<CardModel> list2 = CardFactory.GetDistinctForCombat(card.Owner, (IEnumerable<CardModel>)list, 3, card.Owner.RunState.Rng.CombatCardGeneration).ToList();
                if (list2.Count == 0)
                {
                    Main.Log.Info($"[{"NecroRework"}] 虚空之唤：抽不出可展示的虚无牌（候选 {list.Count} 张），跳过选择", 1);
                }
                else
                {
                    CardModel val = ((list2.Count != 1) ? (await CardSelectCmd.FromChooseACardScreen(ctx, (IReadOnlyList<CardModel>)list2, card.Owner, true)) : list2[0]);
                    CardModel val2 = val;
                    if (val2 != null)
                    {
                        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(val2, (PileType)2, card.Owner, (CardPilePosition)1), 1.2f, (CardPreviewStyle)1);
                    }
                }
            }
        },
        [typeof(Misery)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
                await PowerCmd.Apply<WeakPower>(ctx, target, ((DynamicVar)card.DynamicVars.Weak).BaseValue, card.Owner.Creature, card, false);
                Dictionary<PowerModel, int> debuffAmounts = target.Powers.Where((PowerModel p) =>
                {
                    return (int)p.TypeForCurrentAmount == 2;
                }).Select((PowerModel p) =>
                {
                    return (Power: (PowerModel)((AbstractModel)p).ClonePreservingMutability(), Amount: p.Amount);
                }).ToDictionary(((PowerModel Power, int Amount) x) => x.Power, ((PowerModel Power, int Amount) x) => x.Amount);
                foreach (KeyValuePair<PowerModel, int> item8 in debuffAmounts.ToList())
                {
                    PowerModel key = item8.Key;
                    ITemporaryPower temporary = (ITemporaryPower)(object)((key is ITemporaryPower) ? key : null);
                    if (temporary != null)
                    {
                        KeyValuePair<PowerModel, int> keyValuePair = debuffAmounts.FirstOrDefault((KeyValuePair<PowerModel, int> p) => ((AbstractModel)p.Key).Id == ((AbstractModel)temporary.InternallyAppliedPower).Id);
                        if (keyValuePair.Key != null)
                        {
                            debuffAmounts[keyValuePair.Key] += item8.Value;
                        }
                    }
                }
                foreach (Creature enemy in card.CombatState.HittableEnemies)
                {
                    if (enemy != target)
                    {
                        foreach (KeyValuePair<PowerModel, int> item9 in debuffAmounts)
                        {
                            if (item9.Value != 0)
                            {
                                PowerModel val = PowerCmd.FindExistingInstanceForStacking(item9.Key, enemy, item9.Key.Applier);
                                if (val != null)
                                {
                                    await PowerCmd.ModifyAmount(ctx, val, (decimal)item9.Value, item9.Key.Applier, card, false);
                                }
                                else
                                {
                                    PowerModel val2 = (PowerModel)((AbstractModel)item9.Key).ClonePreservingMutability();
                                    await PowerCmd.Apply(ctx, val2, enemy, (decimal)item9.Value, item9.Key.Applier, card, false);
                                }
                            }
                        }
                    }
                }
            }
        },
        [typeof(TimesUp)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await DamageCmd.Attack(card.DynamicVars.CalculatedDamage).FromCard(card, play).Targeting(target)
                    .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                    .Execute(ctx);
                if (target.IsAlive && DoomUtil.DoomOf(target) <= 0)
                {
                    await PowerCmd.Apply<NeurosurgePower>(ctx, target, 15m, card.Owner.Creature, card, false);
                }
            }
        },
        [typeof(Hang)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await PowerCmd.Apply<WeakPower>(ctx, target, 1m, card.Owner.Creature, card, false);
                await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).FromCard(card, play).Targeting(target)
                    .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                    .Execute(ctx);
                if (target.IsAlive)
                {
                    int powerAmount = target.GetPowerAmount<HangPower>();
                    int num = Math.Max(2, powerAmount);
                    if (powerAmount + num > 999999999)
                    {
                        num = Math.Max(0, 999999999 - powerAmount);
                    }
                    await PowerCmd.Apply<HangPower>(ctx, target, (decimal)num, card.Owner.Creature, card, false);
                }
            }
        },
        [typeof(Eidolon)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            CardPile pile = PileTypeExtensions.GetPile((PileType)4, card.Owner);
            if (pile != null && pile.Cards.Count != 0)
            {
                List<CardModel> list = pile.Cards.Where((CardModel c) => c.Keywords.Contains((CardKeyword)2) && !c.Keywords.Contains((CardKeyword)4)).ToList();
                if (list.Count != 0)
                {
                    foreach (CardModel item10 in (await CardSelectCmd.FromCombatPile(ctx, pile, card.Owner, new CardSelectorPrefs(new LocString("cards", "EIDOLON.selectionScreenPrompt"), 0, list.Count), (Func<CardModel, bool>)((CardModel c) => c.Keywords.Contains((CardKeyword)2) && !c.Keywords.Contains((CardKeyword)4)))).ToList())
                    {
                        CardPile pile2 = item10.Pile;
                        if (pile2 != null && (int)pile2.Type == 4)
                        {
                            await CardCmd.AutoPlay(ctx, item10, (Creature)null, (AutoPlayType)1, false, false);
                        }
                    }
                }
            }
        },
        [typeof(Severance)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            Creature target = play.Target;
            if (target != null)
            {
                await DamageCmd.Attack(((DynamicVar)card.DynamicVars.Damage).BaseValue).FromCard(card, play).Targeting(target)
                    .WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
                    .Execute(ctx);
                List<Soul> souls = Soul.Create(card.Owner, 3, card.CombatState).ToList();
                if (card.IsUpgraded)
                {
                    foreach (Soul item11 in souls)
                    {
                        ((CardModel)item11).UpgradeInternal(false);
                    }
                }
                CardPileAddResult drawResult = await CardPileCmd.AddGeneratedCardToCombat((CardModel)(object)souls[0], (PileType)1, card.Owner, (CardPilePosition)3);
                CardPileAddResult discardResult = await CardPileCmd.AddGeneratedCardToCombat((CardModel)(object)souls[1], (PileType)3, card.Owner, (CardPilePosition)1);
                await CardPileCmd.AddGeneratedCardToCombat((CardModel)(object)souls[2], (PileType)2, card.Owner, (CardPilePosition)1);
                CardCmd.PreviewCardPileAdd((IReadOnlyList<CardPileAddResult>)new CardPileAddResult[2] { drawResult, discardResult }, 1.2f, (CardPreviewStyle)1);
            }
        },
        [typeof(LegionOfBone)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character), Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));
            Creature osty = card.Owner.Osty;
            int ostySummon = ((osty != null && osty.IsAlive) ? osty.CurrentHp : 0);
            List<Creature> allies = card.CombatState.PlayerCreatures.Where((Creature c) => c != null && c.IsAlive).ToList();
            foreach (Creature item12 in allies)
            {
                await OstyCmd.Summon(ctx, item12.Player, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            }
            if (ostySummon > 0)
            {
                foreach (Creature item13 in allies)
                {
                    if (item13 != card.Owner.Creature)
                    {
                        await OstyCmd.Summon(ctx, item13.Player, (decimal)ostySummon, (AbstractModel)(object)card);
                    }
                }
            }
        },
        [typeof(Reanimate)] = async (CardModel card, PlayerChoiceContext ctx, CardPlay play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, Necrobinder.GetSummonAnimIfApplicable(card.Owner.Character), Necrobinder.GetSummonDelayIfApplicable(card.Owner.Character));
            await OstyCmd.Summon(ctx, card.Owner, ((DynamicVar)card.DynamicVars.Summon).BaseValue, (AbstractModel)(object)card);
            await PowerCmd.Apply<ReanimateWardPower>(ctx, card.Owner.Creature, 1m, card.Owner.Creature, card, false);
        }
    };

    public static readonly Dictionary<Type, Action<CardModel>> Upgrade = new Dictionary<Type, Action<CardModel>>
    {
        [typeof(Defile)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Doom).UpgradeValueBy(2m);
        },
        [typeof(Snap)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Cards).UpgradeValueBy(1m);
        },
        [typeof(Invoke)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Summon).UpgradeValueBy(1m);
        },
        // 扫荡凝视：升级 5 → 9（+4，原版是 +5）
        [typeof(SweepingGaze)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.OstyDamage).UpgradeValueBy(4m);
        },
        [typeof(NecroMastery)] = (CardModel card) =>
        {
            card.EnergyCost.UpgradeBy(-1);
        },
        [typeof(Seance)] = (CardModel card) =>
        {
            card.RemoveKeyword((CardKeyword)2);
        },
        [typeof(Defy)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Weak).UpgradeValueBy(1m);
        },
        [typeof(Deathbringer)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Weak).UpgradeValueBy(1m);
        }
    };

    public static readonly Dictionary<Type, Action<CardModel>> UpgradeReplace = new Dictionary<Type, Action<CardModel>>
    {
        [typeof(DevourLife)] = (CardModel card) =>
        {
            card.EnergyCost.UpgradeBy(-1);
        },
        [typeof(SpiritOfAsh)] = (CardModel card) =>
        {
            card.EnergyCost.UpgradeBy(-1);
        },
        [typeof(SentryMode)] = (CardModel _) =>
        {
        },
        [typeof(Scourge)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Doom).UpgradeValueBy(6m);
            card.DynamicVars["Transfer"].UpgradeValueBy(2m);
        },
        [typeof(PullAggro)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Summon).UpgradeValueBy(2m);
            card.DynamicVars["GuardTurns"].UpgradeValueBy(1m);
        },
        [typeof(SicEm)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.OstyDamage).UpgradeValueBy(3m);
            card.DynamicVars["RelentlessPower"].UpgradeValueBy(1m);
        },
        [typeof(Spur)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Heal).UpgradeValueBy(3m);
        },
        [typeof(CaptureSpirit)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Damage).UpgradeValueBy(1m);
            ((DynamicVar)card.DynamicVars.Cards).UpgradeValueBy(1m);
            card.DynamicVars["StrengthLoss"].UpgradeValueBy(1m);
        },
        [typeof(EnfeeblingTouch)] = (CardModel card) =>
        {
            card.DynamicVars["StrengthLoss"].UpgradeValueBy(4m);
        },
        [typeof(Countdown)] = (CardModel card) =>
        {
            card.DynamicVars["CountdownReworkPower"].UpgradeValueBy(3m);
        },
        [typeof(SleightOfFlesh)] = (CardModel card) =>
        {
            card.DynamicVars["SleightOfFleshPower"].UpgradeValueBy(3m);
        },
        [typeof(SoulStorm)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.ExtraDamage).UpgradeValueBy(1m);
        },
        [typeof(Haunt)] = (CardModel _) =>
        {
        },
        [typeof(Calcify)] = (CardModel card) =>
        {
            card.DynamicVars["CalcifyPower"].UpgradeValueBy(2m);
            ((DynamicVar)card.DynamicVars.Block).UpgradeValueBy(1m);
        },
        [typeof(Cleanse)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Doom).UpgradeValueBy(3m);
        },
        [typeof(DrainPower)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Damage).UpgradeValueBy(1m);
        },
        // 得力助手：升级 5 → 7
        [typeof(RightHandHand)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.OstyDamage).UpgradeValueBy(2m);
        },
        [typeof(BansheesCry)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Damage).UpgradeValueBy(8m);
        },
        [typeof(Misery)] = (CardModel card) =>
        {
            card.AddKeyword((CardKeyword)5);
        },
        [typeof(TimesUp)] = (CardModel card) =>
        {
            card.EnergyCost.UpgradeBy(-1);
        },
        [typeof(Hang)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Damage).UpgradeValueBy(2m);
        },
        [typeof(LegionOfBone)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Summon).UpgradeValueBy(3m);
        },
        [typeof(Reanimate)] = (CardModel card) =>
        {
            ((DynamicVar)card.DynamicVars.Summon).UpgradeValueBy(5m);
        }
    };

    public static readonly Dictionary<Type, CardKeyword[]> Keywords = new Dictionary<Type, CardKeyword[]>
    {
        [typeof(Putrefy)] = new CardKeyword[2]
        {
            (CardKeyword)1,
            (CardKeyword)2
        },
        [typeof(SleightOfFlesh)] = new CardKeyword[1] { (CardKeyword)2 },
        [typeof(Shroud)] = new CardKeyword[1] { (CardKeyword)2 },
        [typeof(DeathsDoor)] = new CardKeyword[1] { (CardKeyword)5 },
        [typeof(EndOfDays)] = new CardKeyword[1] { (CardKeyword)5 },
        [typeof(Snap)] = new CardKeyword[1] { (CardKeyword)5 },
        [typeof(Rattle)] = new CardKeyword[1] { (CardKeyword)5 },
        [typeof(Undeath)] = new CardKeyword[1] { (CardKeyword)2 },
        [typeof(TimesUp)] = new CardKeyword[1] { (CardKeyword)5 },
        [typeof(Transfigure)] = new CardKeyword[2]
        {
            (CardKeyword)1,
            (CardKeyword)5
        },
        [typeof(Dirge)] = Array.Empty<CardKeyword>(),
        [typeof(Underworld)] = new CardKeyword[2]
        {
            (CardKeyword)1,
            (CardKeyword)5
        },
        [typeof(Reanimate)] = new CardKeyword[2]
        {
            (CardKeyword)1,
            (CardKeyword)5
        }
    };

    public static readonly Dictionary<Type, CardType> Types = new Dictionary<Type, CardType> { [typeof(Misery)] = (CardType)2 };

    public static readonly Dictionary<Type, int> Costs = new Dictionary<Type, int>
    {
        [typeof(Seance)] = 0,
        [typeof(Shroud)] = 1,
        [typeof(Reanimate)] = 2,
        [typeof(SentryMode)] = 1
    };

    public static readonly Dictionary<Type, Func<CardModel, IEnumerable<IHoverTip>>> Tips = new Dictionary<Type, Func<CardModel, IEnumerable<IHoverTip>>>
    {
        [typeof(Spur)] = (CardModel _) => Array.Empty<IHoverTip>(),
        [typeof(Cleanse)] = (CardModel _) => new IHoverTip[1] { HoverTipFactory.FromKeyword((CardKeyword)1) },
        [typeof(SentryMode)] = (CardModel card) => new IHoverTip[1] { HoverTipFactory.FromCard<SweepingGaze>(card.IsUpgraded) },
        // 大限已至：悬停时把「精神过载」的能力详情窗也列出来（15 层，和卡牌给的一致）
        [typeof(TimesUp)] = (CardModel _) => new IHoverTip[1] { HoverTipFactory.FromPower<NeurosurgePower>(15) }
    };

    public static readonly HashSet<Type> NoGainsBlock = new HashSet<Type> { typeof(PullAggro) };

    private static decimal ProtectorBonus(CardModel card)
    {
        try
        {
            if (((AbstractModel)card).IsCanonical)
            {
                return 0m;
            }
            Player owner = card.Owner;
            if (((owner != null) ? owner.PlayerCombatState : null) == null)
            {
                return 0m;
            }
            decimal num = Math.Floor((decimal)CombatStats.TotalSummon(card) / 2m) * 2m;
            decimal num2 = (decimal)ExhaustCountTracker.TotalFor(card.Owner) * 2m;
            return num + num2;
        }
        catch (Exception value)
        {
            Main.Log.Error($"[{"NecroRework"}] 守护者伤害计算失败: {value}", 1);
            return 0m;
        }
    }

    private static int RattleHits(CardModel card)
    {
        try
        {
            if (((AbstractModel)card).IsCanonical)
            {
                return 1;
            }
            Player owner = card.Owner;
            if (((owner != null) ? owner.PlayerCombatState : null) == null)
            {
                return 1;
            }
            int num = OstyDamageTracker.TotalFor(card.Owner);
            if (num <= 0)
            {
                return 1;
            }
            int num2 = (card.IsUpgraded ? 14 : 15);
            return 1 + num / num2;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static decimal SpurBlockPreview(CardModel card)
    {
        if (((AbstractModel)card).IsCanonical)
        {
            return 0m;
        }
        Player owner = card.Owner;
        if (owner == null)
        {
            return 0m;
        }
        decimal num = OstyHpGainTracker.GainedThisTurn(owner);
        Creature osty = owner.Osty;
        if (osty == null || !osty.IsAlive)
        {
            return num;
        }
        decimal val = Math.Max(0m, osty.MaxHp - osty.CurrentHp);
        decimal val2 = Math.Min(((DynamicVar)card.DynamicVars.Heal).BaseValue, val);
        return num + Math.Max(0m, val2);
    }

    private static decimal SacrificeBlockFrom(CardModel card)
    {
        if (((AbstractModel)card).IsCanonical)
        {
            return 0m;
        }
        if (!card.Owner.IsOstyAlive)
        {
            return 0m;
        }
        Creature osty = card.Owner.Osty;
        if (osty == null || !osty.IsAlive)
        {
            return 0m;
        }
        return Math.Floor((decimal)osty.CurrentHp * 0.75m);
    }

    private static decimal CleanseBlockPreview(CardModel card)
    {
        if (((AbstractModel)card).IsCanonical)
        {
            return 0m;
        }
        int num = (int)((DynamicVar)card.DynamicVars.Doom).BaseValue;
        if (num <= 0)
        {
            return 0m;
        }
        int num2 = DoomUtil.PlayerDoom(card);
        Creature osty = card.Owner.Osty;
        if (osty != null && osty.IsAlive)
        {
            num2 += DoomUtil.DoomOf(osty);
        }
        return Math.Min(num, Math.Max(0, num2));
    }

    private static int SqueezeCount(CardModel card)
    {
        if (((AbstractModel)card).IsCanonical)
        {
            return 0;
        }
        Player owner = card.Owner;
        PlayerCombatState val = ((owner != null) ? owner.PlayerCombatState : null);
        if (val == null)
        {
            return 0;
        }
        return val.AllCards.Count((CardModel c) => c != card && c.Tags.Contains((CardTag)4));
    }

    private static int SoulStormFuel(CardModel card)
    {
        if (((AbstractModel)card).IsCanonical)
        {
            return 0;
        }
        Player owner = card.Owner;
        object obj;
        if (owner == null)
        {
            obj = null;
        }
        else
        {
            PlayerCombatState playerCombatState = owner.PlayerCombatState;
            obj = ((playerCombatState != null) ? playerCombatState.ExhaustPile : null);
        }
        CardPile val = (CardPile)obj;
        if (val == null)
        {
            return 0;
        }
        int num = 0;
        foreach (CardModel card2 in val.Cards)
        {
            if (card2 is Soul || card2.Enchantment is DriftEnchantment)
            {
                num++;
            }
        }
        return num;
    }
}
