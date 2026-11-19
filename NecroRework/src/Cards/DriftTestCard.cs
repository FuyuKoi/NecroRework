using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NecroRework.Enchantments;

namespace NecroRework.Cards;

/// <summary>
/// 调试用卡：给手牌里第一张还没有附魔的牌附上「飘荡」。
/// 等正式卡表定下来之后会删掉。
/// </summary>
public sealed class DriftTestCard : CardModel
{
    public DriftTestCard()
        : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        var target = PileType.Hand.GetPile(Owner).Cards.FirstOrDefault(c => c.Enchantment == null);
        if (target == null) return;

        CardCmd.Enchant(ModelDb.Enchantment<DriftEnchantment>().ToMutable(), target, 1m);
        CardCmd.Preview(target);
    }

    protected override void OnUpgrade()
    {
    }
}
