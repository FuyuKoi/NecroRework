using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NecroRework.Powers;

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
    };
}
