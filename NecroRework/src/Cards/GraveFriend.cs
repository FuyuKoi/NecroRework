using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace NecroRework.Cards;

/// <summary>
/// 墓友：1 费技能，虚无。
/// 从抽牌堆里选一张「费用不高于此牌当前费用」的牌置入手牌，
/// 它在下次打出之前的费用减少 1（升级后减少 2）。
/// </summary>
public sealed class GraveFriend : CardModel
{
    public GraveFriend()
        : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    /// <summary>减费点数：1（升级后 2）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        int cap = EnergyCost.GetResolved();

        var prefs = new CardSelectorPrefs(SelectionScreenPrompt, 1);

        // 选牌期间让抽牌堆里的牌按「原本的费用」显示与判定：
        // 预借时间的全局加费不参与抽牌堆里的牌（手牌与打出区照旧，所以墓友自己的 cap 仍带加成）。
        CostPreview.Begin();
        try
        {
            var picked = (await CardSelectCmd.FromCombatPile(
                choiceContext, PileType.Draw.GetPile(Owner), Owner, prefs,
                c => !c.EnergyCost.CostsX && c.EnergyCost.GetWithModifiers(CostModifiers.Local) <= cap)).FirstOrDefault();

            if (picked == null) return;

            await CardPileCmd.Add(picked, PileType.Hand);

            int discount = DynamicVars.Energy.IntValue;
            picked.EnergyCost.AddUntilPlayed(-discount, reduceOnly: true);
            picked.InvokeEnergyCostChanged();
            CardCmd.Preview(picked);
        }
        finally
        {
            CostPreview.End();
        }
    }

    protected override void OnUpgrade()
    {
        // 升级：选中的牌减费 1 → 2（自身费用不变）。
        DynamicVars.Energy.UpgradeValueBy(1m);
    }
}
