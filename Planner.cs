using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace RngPreview;

/// <summary>
/// Something that rolls: playing a card, using a potion, ending the turn, starting the next combat.
/// <see cref="Preview"/>, if set, is what its row shows instead of <see cref="Run"/>'s result: a power card rolls
/// nothing when played, so its row shows what its power will roll.
/// </summary>
internal sealed record Step(string Label, Func<Sim, List<Roll>> Run, Func<Sim, List<Roll>>? Preview = null)
{
    /// <summary>For a card or potion: which copies it uses up, and how many there are. Null for repeatable steps.</summary>
    public string? CopiesKey { get; init; }

    public int Copies { get; init; }

    /// <summary>Ends this combat and starts the next one ("Win this combat").</summary>
    public bool StartsNextCombat { get; init; }

    /// <summary>The card a "Play" step plays.</summary>
    public CardModel? Card { get; init; }

    /// <summary>The played card is gone for this fight (a power, or it exhausts) rather than going to the discard pile.</summary>
    public bool LeavesFight { get; init; }

    /// <summary>Every copy is already planned but the card comes back (discard pile), so it's shown grayed out.</summary>
    public bool Spent { get; init; }
}

/// <summary>A generator card that can't be used in this fight, and why (exhausted, stolen, ...).</summary>
internal sealed record Elsewhere(string Label, CardModel Card);

/// <summary>
/// The planned order of rolls for this turn, and what each available option would give if it came next. The plan
/// is relative to the generators' state when it was made, so it is dropped as soon as the real game rolls.
/// </summary>
internal static class Planner
{
    public static readonly List<Step> Plan = new List<Step>();

    private static (int, int) _planCounters;
    private static int _planTurn;
    private static CombatState? _planCombat;

    /// <summary>Drops the plan if the game has rolled, the turn changed or a new combat started since it was made.</summary>
    public static void Validate(Player player)
    {
        (int, int) counters = Sim.Counters(player);
        int turn = player.PlayerCombatState?.TurnNumber ?? 0;
        CombatState? combat = CombatManager.Instance.DebugOnlyGetState();
        if (counters != _planCounters || turn != _planTurn || combat != _planCombat)
        {
            Plan.Clear();
            _planCounters = counters;
            _planTurn = turn;
            _planCombat = combat;
        }
    }

    public static List<Step> Options(Player player)
    {
        List<Step> options = new List<Step>();
        IEnumerable<(CardModel card, PileType pile)> cards = new[] { PileType.Hand, PileType.Draw, PileType.Discard }
            .SelectMany(t => t.GetPile(player).Cards.Select(c => (c, t)))
            .Where(x => Effects.IsGenerator(x.c));
        foreach (IGrouping<string, (CardModel card, PileType pile)> group in cards.GroupBy(x => x.card.Title))
        {
            CardModel card = group.First().card;
            string where = string.Join(", ", group.GroupBy(x => x.pile).Select(g => $"{PileName(g.Key)} x{g.Count()}"));
            options.Add(new Step($"Play {group.Key}  ({where})", s => Effects.PlayCard(s, card),
                Effects.IsPowerCard(card) ? s => Effects.PreviewPowerCard(s, card) : null)
            {
                CopiesKey = "card:" + group.Key,
                Copies = group.Count(),
                Card = card,
                // Soul's Power and some upgrades (Discovery+) remove Exhaust; Keywords reflects that.
                LeavesFight = card.Type == CardType.Power || card.Keywords.Contains(CardKeyword.Exhaust),
            });
        }
        foreach (IGrouping<string, PotionModel> group in player.PotionSlots.OfType<PotionModel>()
                     .Where(Effects.IsGenerator).GroupBy(p => p.Title.GetFormattedText()))
        {
            PotionModel potion = group.First();
            options.Add(new Step($"Use {group.Key}  (x{group.Count()})", s => Effects.UsePotion(s, potion))
            {
                CopiesKey = "potion:" + group.Key,
                Copies = group.Count(),
            });
        }
        // Shown when Calamity is active, or could be by playing a Calamity card in the plan.
        if (player.Creature.Powers.Any(p => p is MegaCrit.Sts2.Core.Models.Powers.CalamityPower) ||
            options.Any(o => o.Label.StartsWith("Play Calamity")))
        {
            options.Add(new Step("Play any other Attack (Calamity)", s => Effects.Calamity(s) is { } calamity
                ? new List<Roll> { Effects.CalamityRoll(s, calamity) }
                : new List<Roll> { new Roll(null, "Calamity", Array.Empty<Item>(), "not active yet") }));
        }
        options.Add(new Step("End turn (enemy turn + start of next turn)", Effects.EndTurn));
        options.Add(new Step("Win this combat (start of next combat)", Effects.NextCombat) { StartsNextCombat = true });
        return WithoutPlannedCopies(options);
    }

    /// <summary>
    /// Each planned card or potion uses up one copy, and the row says how many are planned. Once every copy is planned:
    /// potions, powers and Exhaust cards leave "Next" (cards move to "Not in this fight"); other cards go to the discard
    /// pile and could be reshuffled and played again, so they stay at the bottom, grayed out but still clickable.
    /// </summary>
    private static List<Step> WithoutPlannedCopies(List<Step> options)
    {
        List<Step> left = new List<Step>();
        List<Step> spent = new List<Step>();
        foreach (Step option in options)
        {
            int planned = option.CopiesKey == null ? 0 : Plan.Count(p => p.CopiesKey == option.CopiesKey);
            if (planned == 0)
            {
                left.Add(option);
            }
            else if (planned < option.Copies)
            {
                left.Add(option with { Label = $"{option.Label}, {planned} planned" });
            }
            else if (option.Card != null && !option.LeavesFight)
            {
                spent.Add(option with { Label = $"{option.Label}, {planned} planned: in the discard pile, playable after a reshuffle", Spent = true });
            }
        }
        left.AddRange(spent);
        return left;
    }

    private static string PileName(PileType pile) => pile switch
    {
        PileType.Hand => "hand",
        PileType.Draw => "draw pile",
        PileType.Discard => "discard",
        _ => pile.ToString(),
    };

    /// <summary>
    /// Generator cards in your deck that can't be played in this fight: exhausted, played as a power, transformed or
    /// otherwise out of your hand, draw and discard piles. Plus cards a living Thieving Hopper stole (removed from your
    /// deck; you get them back as a reward only if you kill it). Generated cards don't last past this fight, so
    /// they're not listed.
    /// </summary>
    public static List<Elsewhere> NotInThisFight(Player player)
    {
        HashSet<CardModel> usable = new[] { PileType.Hand, PileType.Draw, PileType.Discard }
            .SelectMany(t => t.GetPile(player).Cards).Where(c => c.DeckVersion != null).Select(c => c.DeckVersion!).ToHashSet();
        List<CardModel> exhausted = PileType.Exhaust.GetPile(player).Cards.Where(c => c.DeckVersion != null)
            .Select(c => c.DeckVersion!).ToList();
        List<(string reason, CardModel card)> cards = player.Deck.Cards
            .Where(d => Effects.IsGenerator(d) && !usable.Contains(d))
            .Select(d => (exhausted.Contains(d) ? "exhausted"
                : d.Type == CardType.Power ? "played" : "not in this fight", d))
            .ToList();
        IEnumerable<CardModel> stolen = (CombatManager.Instance.DebugOnlyGetState()?.Enemies ?? Enumerable.Empty<Creature>())
            .Where(e => !e.IsDead)
            .SelectMany(e => e.Powers.OfType<SwipePower>())
            .Select(p => p.StolenCard).OfType<CardModel>()
            .Where(c => c.Owner == player && Effects.IsGenerator(c));
        cards.AddRange(stolen.Select(c => ("stolen: kill the Thieving Hopper to get it back", c)));
        // Copies the plan plays that won't come back this fight.
        cards.AddRange(Plan.Where(s => s.LeavesFight && s.Card != null)
            .Select(s => (s.Card!.Type == CardType.Power ? "planned: played as a power" : "planned: exhausts", s.Card!)));
        return cards.GroupBy(x => (x.card.Title, x.reason))
            .Select(g => new Elsewhere($"{g.Key.Title}  ({g.Key.reason}{(g.Count() > 1 ? $", x{g.Count()}" : "")})", g.First().card))
            .ToList();
    }

    /// <summary>
    /// The plan's results in order; each option's result if it came right after the plan; and for each card not in
    /// this fight, what it gives as the first roll of the next combat (after the plan and the next combat's start).
    /// </summary>
    public static (List<(Step step, List<Roll> rolls)> planned, List<(Step step, List<Roll> rolls)> next,
        List<(Elsewhere card, List<Roll> rolls)> nextCombat) Evaluate(Player player)
    {
        Sim sim = Sim.Live(player);
        List<(Step, List<Roll>)> planned = Plan.Select(step => (step, step.Run(sim))).ToList();
        List<(Step, List<Roll>)> next = Options(player).Select(step => (step, (step.Preview ?? step.Run)(sim.Clone()))).ToList();

        Sim nextCombatStart = sim.Clone();
        if (!Plan.Any(step => step.StartsNextCombat))
        {
            Effects.NextCombat(nextCombatStart);
        }
        List<(Elsewhere, List<Roll>)> later = NotInThisFight(player).Select(e =>
        {
            Sim s = nextCombatStart.Clone();
            return (e, Effects.IsPowerCard(e.Card)
                ? Effects.PreviewPowerCard(s, e.Card, "start of next turn, given the combat ends this turn")
                : Effects.PlayCard(s, e.Card));
        }).ToList();
        return (planned, next, later);
    }
}
