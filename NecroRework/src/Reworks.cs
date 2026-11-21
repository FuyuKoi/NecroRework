using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
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

        // 剥夺：伤害 10（升级 13）→ 11（升级 14）；灵魂部分保持原版（升级后给灵魂+）
        [typeof(Reave)] = _ => [new DamageVar(11m, ValueProp.Move), new CardsVar(1)],

        // 玷污：伤害 13（升级 17）→ 12（升级 16）；额外转移至多 6（升级 8）层灾厄
        [typeof(Defile)] = _ => [new DamageVar(12m, ValueProp.Move), new PowerVar<DoomPower>(6m)],

        // 荒疫打击：伤害 8（升级 10）→ 10（升级 12）；开局先获得 5 点灾厄（固定值）
        [typeof(BlightStrike)] = _ => [new DamageVar(10m, ValueProp.Move)],

        // 响指：奥斯提伤害 7（升级 10）不变；保留张数 1（升级 2）
        [typeof(Snap)] = _ => [new OstyDamageVar(7m, ValueProp.Move), new CardsVar(1)],

        // 鬼火：获得 1 费 → 获得 2 费（消耗、升级给保留都不变）
        [typeof(Wisp)] = _ => [new EnergyVar(2)],

        // 鞭打：给予 13（升级 16）层灾厄、抽 1（升级 2）张
        //       → 给予 14（升级 17）层灾厄、抽 2 张（张数不随升级涨，见 UpgradeReplace）
        [typeof(Scourge)] = _ => [new PowerVar<DoomPower>(14m), new CardsVar(2)],

        // 负能量脉冲：格挡 5（升级 6）、全体 7（升级 11）层灾厄
        //             → 格挡 7（升级 8）、全体 9（升级 13）层灾厄
        [typeof(NegativePulse)] = _ => [new BlockVar(7m, ValueProp.Move), new PowerVar<DoomPower>(9m)],

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
                CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>()
                    .Count(e => e.CardPlay.Player == card.Owner && e.WasEthereal))
        ],

        // 忧郁：格挡 13（升级 17）→ 12（升级 16）；降费条件换成「你或奥斯提获得负面效果」
        [typeof(Melancholy)] = _ => [new BlockVar(12m, ValueProp.Move), new EnergyVar(1)],

        // 领会：抽 3（升级 4）→ 先获得 6 层灾厄，再抽 3（升级 4）
        [typeof(Parse)] = _ => [new CardsVar(3), new PowerVar<DoomPower>(6m)],

        // 挽歌：每次召唤 3（升级 4）→ 5（升级 6）；灵魂张数改为 X+2（Play 里加）
        [typeof(Dirge)] = _ => [new SummonVar(5m)],

        // 增生：去掉召唤，只保留治疗 5（升级 8）；
        //       SummonVar(0) 只是留着给原版悬停提示取用，卡面上不会显示
        [typeof(Spur)] = _ => [new HealVar(5m), new SummonVar(0m)],

        // 腐烂：每次施加的层数 2（升级 3）→ 1（升级 2），实际施加两次
        [typeof(Putrefy)] = _ => [new DynamicVar("Power", 1m)],

        // 倒数计时：随机敌人灾厄 6（升级 9）→ 4（升级 7）；行为换成自制能力
        [typeof(Countdown)] = _ => [new PowerVar<CountdownReworkPower>(4m)],

        // 血肉戏法：9（升级 13）→ 5（升级 8），再加一个「虚无」
        [typeof(SleightOfFlesh)] = _ => [new PowerVar<SleightOfFleshPower>(5m)],

        // 纠缠：灵魂造成的生命流失 7（升级 9）→ 固定 8；额外生成 1 张灵魂
        [typeof(Haunt)] = _ => [new HpLossVar(8m)],

        // 钙化：奥斯提攻击加成 4（升级 6）→ 3（升级 5）；
        //       并额外记录「每次攻击给你 2（升级 3）格挡」
        [typeof(Calcify)] = _ => [new PowerVar<CalcifyPower>(3m), new BlockVar(2m, ValueProp.Unpowered)],

        // 洁净：召唤 3（升级 5）→ 改成「移除 6（升级 9）层灾厄」，数值用灾厄变量显示
        [typeof(Cleanse)] = _ => [new PowerVar<DoomPower>(6m)],
    };

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

        // 荒疫打击：先给自己上 5 点灾厄（触发死亡之门 / 厄运之衣），再造成伤害，
        // 最后把「实际造成的伤害」等量转成敌人身上的灾厄。
        [typeof(BlightStrike)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await DoomUtil.ApplyToSelf(ctx, card, 5m);

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

        // 鬼火：获得 2 费，然后获得 6 层灾厄（会触发死亡之门 / 厄运之衣）。
        [typeof(Wisp)] = async (card, ctx, play) =>
        {
            await PlayerCmd.GainEnergy(card.DynamicVars.Energy.IntValue, card.Owner);
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
        },

        // 鞭打：先把至多 7 层灾厄从自己（角色 → 奥斯提）转给目标，再给目标上 14 层，最后抽 2 张。
        [typeof(Scourge)] = async (card, ctx, play) =>
        {
            if (play.Target is not { } target) return;

            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.TransferToEnemy(ctx, card, target, 7m);
            await PowerCmd.Apply<DoomPower>(ctx, target, card.DynamicVars.Doom.BaseValue, card.Owner.Creature, card);
            await CardPileCmd.Draw(ctx, card.DynamicVars.Cards.BaseValue, card.Owner);
        },

        // 负能量脉冲：先给自己 6 层灾厄，再起格挡，最后给所有敌人上灾厄。
        [typeof(NegativePulse)] = async (card, ctx, play) =>
        {
            await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
            await DoomUtil.ApplyToSelf(ctx, card, 6m);
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
            await DoomUtil.ApplyToSelf(ctx, card, 8m);
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

            CardModel? picked = (await CardSelectCmd.FromCombatPile(ctx, PileType.Draw.GetPile(card.Owner), card.Owner,
                new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1))).FirstOrDefault();
            if (picked != null)
            {
                await CardCmd.Exhaust(ctx, picked);
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
        // 鞭打：原版升级会「灾厄 +3、抽牌 +1」，我们只要灾厄 14 → 17，抽牌固定 2 张
        [typeof(Scourge)] = card => card.DynamicVars.Doom.UpgradeValueBy(3m),

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

        // 倒数计时：原版升级去取 "CountdownPower" 这个已经不存在的键，这里自己 +3
        [typeof(Countdown)] = card => card.DynamicVars["CountdownReworkPower"].UpgradeValueBy(3m),

        // 血肉戏法：原版 +4，现在 5 → 8 是 +3
        [typeof(SleightOfFlesh)] = card => card.DynamicVars["SleightOfFleshPower"].UpgradeValueBy(3m),

        // 纠缠：生命流失固定 8 不涨，升级只把生成的灵魂换成灵魂+
        [typeof(Haunt)] = _ => { },

        // 钙化：原版只 +2 伤害；现在伤害 +2（3 → 5）、格挡 +1（2 → 3）
        [typeof(Calcify)] = card =>
        {
            card.DynamicVars["CalcifyPower"].UpgradeValueBy(2m);
            card.DynamicVars.Block.UpgradeValueBy(1m);
        },

        // 洁净：原版是召唤 3 → 5，现在改成灾厄 6 → 9
        [typeof(Cleanse)] = card => card.DynamicVars.Doom.UpgradeValueBy(3m),
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
    };

    /// <summary>
    /// 改费用（原版费用写在构造函数里，这里覆盖 CanonicalEnergyCost）。
    /// </summary>
    public static readonly Dictionary<Type, int> Costs = new()
    {
        // 厄运之衣：1 费 → 2 费
        [typeof(Shroud)] = 2,
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
}
