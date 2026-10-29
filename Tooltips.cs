using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using ModToggle;

namespace RngPreview;

/// <summary>
/// Adds "what this will generate next" to the hover of every generator: cards in your combat piles, your potions,
/// relics (Toolbox, Crossbow, Delicate Frond, ...) and powers (Creative AI, Calamity, ...). The prediction is
/// computed when the hover opens, from the generators' current state.
/// </summary>
internal static class Tooltips
{
    private const string TitleKey = "RNG_PREVIEW.NEXT_ROLL";

    /// <summary>Card previews shown per tooltip; bigger rolls (Stoke, Metamorphosis) are listed as text only.</summary>
    private const int MaxCardPreviews = 5;

    private static IEnumerable<IHoverTip> For(string header, IReadOnlyList<Roll> rolls)
    {
        List<IHoverTip> tips = new List<IHoverTip>();
        IEnumerable<string> lines = rolls.Select(Describe);
        tips.Add(new HoverTip(Title(), header + "\n" + string.Join("\n", lines)));
        List<Item> items = rolls.SelectMany(r => r.Items).ToList();
        if (items.Count(i => i.Card != null) <= MaxCardPreviews)
        {
            tips.AddRange(items.Where(i => i.Card != null).Select(i => HoverTipFactory.FromCard(i.Card!, i.ShowUpgraded)));
        }
        tips.AddRange(items.Where(i => i.Potion != null).Select(i => HoverTipFactory.FromPotion(i.Potion!)));
        return tips;
    }

    private static string Describe(Roll roll)
    {
        string items = roll.Items.Count == 0 ? "" : string.Join(", ", roll.Items.Select(i => i.Name));
        string note = roll.Note == null ? "" : $" ({roll.Note})";
        return $"[gold]{roll.Label}[/gold]: {(items.Length == 0 ? "nothing" : items)}{note}";
    }

    private static LocString Title()
    {
        LocTable table = LocManager.Instance.GetTable("card_selection");
        if (!table.IsLocalKey(TitleKey))
        {
            table.MergeWith(new Dictionary<string, string> { [TitleKey] = "Next random roll" });
        }
        return new LocString("card_selection", TitleKey);
    }

    private static void Add(ref IEnumerable<IHoverTip> result, Func<IEnumerable<IHoverTip>?> extra)
    {
        if (!ToggleSettings.Enabled)
        {
            return;
        }
        try
        {
            IEnumerable<IHoverTip>? tips = extra();
            if (tips != null)
            {
                result = result.Concat(tips).ToList();
            }
        }
        catch (Exception e)
        {
            Log.Error("[RngPreview] tooltip failed: " + e);
        }
    }

    private const string IfNext = "If nothing else rolls first:";

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
    private static class CardTips
    {
        private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            CardModel card = __instance;
            Add(ref __result, () =>
            {
                if (!card.IsMutable || card.Pile is not { IsCombatPile: true } || !CombatManager.Instance.IsInProgress ||
                    !Effects.IsGenerator(card))
                {
                    return null;
                }
                if (Effects.IsPowerCard(card))
                {
                    return For(card is MegaCrit.Sts2.Core.Models.Cards.Calamity
                        ? "If you play this, then an Attack, with nothing else rolling first:"
                        : "If you play this and end the turn, with nothing else rolling first:",
                        Effects.PreviewPowerCard(Sim.Live(card.Owner), card));
                }
                return For("If you play this next:", Effects.PlayCard(Sim.Live(card.Owner), card));
            });
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
    private static class PotionTips
    {
        private static void Postfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            PotionModel potion = __instance;
            Add(ref __result, () =>
            {
                if (!potion.IsMutable || !Effects.IsGenerator(potion) || potion.Owner is not { } owner ||
                    !owner.PotionSlots.Contains(potion))
                {
                    return null;
                }
                return For("If you use this next:", Effects.UsePotion(Sim.Live(owner), potion));
            });
        }
    }

    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
    private static class RelicTips
    {
        private static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            RelicModel relic = __instance;
            Add(ref __result, () =>
            {
                if (!relic.IsMutable || relic is not (Toolbox or BigHat or OrangeDough or ChoicesParadox or VexingPuzzlebox
                        or DelicateFrond or Crossbow) || relic.Owner is not { } owner || !owner.Relics.Contains(relic))
                {
                    return null;
                }
                Sim sim = Sim.Live(owner);
                bool nextCombat = sim.Turn == 0 || relic is not Crossbow;
                Roll? roll = Effects.NextPassiveRoll(sim, relic);
                return roll == null ? null : For(nextCombat ? "Next combat, " + IfNext : "Next turn, " + IfNext, new[] { roll });
            });
        }
    }

    /// <summary>The card a Thieving Hopper about to steal will take, or null.</summary>
    private static Roll? StealRoll(Creature hopper)
    {
        if (!CombatManager.Instance.IsInProgress || !Sim.IsAboutToSteal(hopper) ||
            CombatManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(p => LocalContext.IsMe(p)) is not { } player)
        {
            return null;
        }
        return Effects.EndTurn(Sim.Live(player)).FirstOrDefault(r => ReferenceEquals(r.Source, hopper));
    }

    // Hovering the Thieving Hopper itself: a tip with a preview of the card it will steal.
    [HarmonyPatch(typeof(Creature), nameof(Creature.HoverTips), MethodType.Getter)]
    private static class CreatureTips
    {
        private static void Postfix(Creature __instance, ref IEnumerable<IHoverTip> __result)
        {
            Creature creature = __instance;
            Add(ref __result, () => StealRoll(creature) is { } roll
                ? For("If you end the turn with this hand and nothing else rolls first:", new[] { roll })
                : null);
        }
    }

    // Hovering its steal intent: the intent tip is a single tip, so the stolen card is added to its text.
    [HarmonyPatch(typeof(AbstractIntent), nameof(AbstractIntent.GetHoverTip))]
    private static class IntentTip
    {
        private static readonly PropertyInfo Description = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description));

        private static void Postfix(AbstractIntent __instance, Creature owner, ref HoverTip __result)
        {
            try
            {
                if (!ToggleSettings.Enabled || __instance is not CardDebuffIntent || StealRoll(owner) is not { } roll)
                {
                    return;
                }
                object boxed = __result;
                Description.SetValue(boxed, __result.Description + "\n\n" + Describe(roll) +
                                            "\n(if you end the turn with this hand and nothing else rolls first)");
                __result = (HoverTip)boxed;
            }
            catch (Exception e)
            {
                Log.Error("[RngPreview] intent tooltip failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
    private static class PowerTips
    {
        private static void Postfix(PowerModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            PowerModel power = __instance;
            Add(ref __result, () =>
            {
                if (!power.IsMutable || !CombatManager.Instance.IsInProgress || power.Owner?.Player is not { } player ||
                    !player.Creature.Powers.Contains(power))
                {
                    return null;
                }
                Sim sim = Sim.Live(player);
                if (power is CalamityPower && Effects.Calamity(sim) is { } calamity)
                {
                    return For("Your next Attack, " + IfNext, new[] { Effects.CalamityRoll(sim, calamity) });
                }
                if (power is not (CallOfTheVoidPower or CreativeAiPower or HelloWorldPower or SpectrumShiftPower))
                {
                    return null;
                }
                Roll? roll = Effects.NextPassiveRoll(sim, power);
                return roll == null ? null : For("Next turn, " + IfNext, new[] { roll });
            });
        }
    }
}
