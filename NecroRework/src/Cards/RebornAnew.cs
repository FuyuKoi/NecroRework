using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NecroRework.Cards;

/// <summary>
/// 脱胎换骨：多人专用蓝色技能。1 费、保留、消耗。
/// 让所有玩家（含各自的奥斯提）身上的可叠加负面效果各减少 1 层。
/// 力量/敏捷在原版里无论正负都属于「正面效果」，所以只在数值为负时 +1（把负值往回拉一层）。
/// 升级：去掉消耗。
/// </summary>
public sealed class RebornAnew : CardModel
{
    public RebornAnew() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay play)
    {
        try
        {
            await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

            if (CombatState is not { } state) return;

            int cleansed = 0;

            foreach (Creature creature in state.Allies.Where(c => c.IsAlive).ToList())
            {
                foreach (PowerModel power in creature.Powers.ToList())
                {
                    // 力量 / 敏捷：原版把它们归为正面效果，只有负值才往回拉一层。
                    if (power is StrengthPower or DexterityPower)
                    {
                        if (power.Amount < 0)
                        {
                            await PowerCmd.ModifyAmount(choiceContext, power, 1m, null, this);
                            cleansed++;
                        }

                        continue;
                    }

                    // 只处理「负面效果 + 可叠加」；不可叠加的（恶咒、抑制这类）跳过。
                    if (power.Type != PowerType.Debuff) continue;
                    if (power.StackType != PowerStackType.Counter) continue;
                    if (power.Amount <= 0) continue;

                    // applier 传 null：纯减少，不触发任何「施加」判定。
                    await PowerCmd.ModifyAmount(choiceContext, power, -1m, null, this);
                    cleansed++;
                }
            }

            Main.Log.Info($"脱胎换骨：全队共减少 {cleansed} 层负面效果");
        }
        catch (Exception e)
        {
            Main.Log.Warn($"脱胎换骨 出牌异常：{e}");
        }
    }

    protected override void OnUpgrade()
    {
        // 升级：去掉消耗。
        RemoveKeyword(CardKeyword.Exhaust);
    }
}
