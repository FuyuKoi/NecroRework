using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NecroRework.Cards;

/// <summary>
/// 弥合：2 费技能，获得格挡；在本回合保留至多 3 张手牌，
/// 其中带「虚无」的牌本回合不会自我消耗。
/// </summary>
public sealed class Mend : CardModel
{
    /// <summary>最多能选几张手牌。</summary>
    public const int MaxPick = 3;

    private const decimal BaseBlock = 12m;
    private const decimal BlockOnUpgrade = 4m;

    public Mend() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(BaseBlock, ValueProp.Move)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        IEnumerable<CardModel> selected = await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 0, MaxPick),
            null,
            this);

        foreach (CardModel picked in selected)
        {
            // 只在本回合有效：引擎会在回合结束时清掉这个标记
            picked.GiveSingleTurnRetain();

            if (picked.Keywords.Contains(CardKeyword.Ethereal))
            {
                MendWard.SuppressEthereal(picked);
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(BlockOnUpgrade);
    }
}
