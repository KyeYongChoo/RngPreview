using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace RngPreview;

/// <summary>A generated card (possibly shown upgraded) or potion.</summary>
internal sealed record Item(CardModel? Card, PotionModel? Potion, bool Upgraded)
{
    /// <summary>An existing card (e.g. one Thieving Hopper will steal), shown as its canonical version.</summary>
    public static Item Existing(CardModel card) => new Item(card.CanonicalInstance, null, card.IsUpgraded);

    public bool ShowUpgraded => Card != null && Upgraded && Card.IsUpgradable;

    public string Name => Card != null
        ? Card.TitleLocString.GetFormattedText() + (ShowUpgraded ? "+" : "")
        : Potion!.Title.GetFormattedText();

    public object Rarity => Card != null ? Card.Rarity : Potion!.Rarity;
}

/// <summary>What one source generated. <see cref="Source"/> is the card, potion, relic, power or monster.</summary>
internal sealed record Roll(object? Source, string Label, IReadOnlyList<Item> Items, string? Note = null);

/// <summary>
/// A stack of one of the powers that roll (Creative AI, Calamity, ...), as it will be on the player.
/// <see cref="Key"/> is the real power, or the card that would apply it when the plan plays one.
/// </summary>
internal sealed class PowerSlot(Type type, int amount, object key)
{
    public Type Type { get; } = type;
    public int Amount { get; set; } = amount;
    public object Key { get; } = key;
}

/// <summary>
/// A copy of the two run-wide generators that combat card and potion generation draw from, plus the little state
/// that changes how much a generator draws (open potion slots, turn number, a Thieving Hopper about to steal).
/// Rolling on a Sim never touches the real game: the generators are copies and only canonical models are returned.
/// </summary>
internal sealed class Sim
{
    private static readonly Func<IRunState, IEnumerable<CardModel>, IEnumerable<CardModel>> FilterForPlayerCount =
        AccessTools.MethodDelegate<Func<IRunState, IEnumerable<CardModel>, IEnumerable<CardModel>>>(
            AccessTools.Method(typeof(CardFactory), "FilterForPlayerCount"));

    public Player Player { get; }

    /// <summary>Copy of RunState.Rng.CombatCardGeneration.</summary>
    public Rng Cards { get; private set; }

    /// <summary>Copy of RunState.Rng.CombatPotionGeneration.</summary>
    public Rng Potions { get; private set; }

    public int OpenPotionSlots { get; set; }

    /// <summary>The player's turn number in the current combat (0 outside combat).</summary>
    public int Turn { get; set; }

    /// <summary>Thieving Hoppers whose next move is the steal (each draws once from the card generator).</summary>
    public List<Creature> Thieves { get; private set; } = new List<Creature>();

    /// <summary>The player's rolling powers in the order they were applied (the order they roll in).</summary>
    public List<PowerSlot> Powers { get; private set; } = new List<PowerSlot>();

    private Sim(Player player, Rng cards, Rng potions)
    {
        Player = player;
        Cards = cards;
        Potions = potions;
    }

    public static Sim Live(Player player)
    {
        RunRngSet rng = player.RunState.Rng;
        Sim sim = new Sim(player, Copy(rng.CombatCardGeneration), Copy(rng.CombatPotionGeneration))
        {
            OpenPotionSlots = player.PotionSlots.Count(p => p == null),
            Powers = player.Creature.Powers.Where(p => Effects.RollingPowers.Contains(p.GetType()))
                .Select(p => new PowerSlot(p.GetType(), p.Amount, p)).ToList(),
        };
        CombatState? combat = CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState() : null;
        if (combat != null && player.PlayerCombatState != null)
        {
            sim.Turn = player.PlayerCombatState.TurnNumber;
            sim.Thieves = combat.Enemies.Where(IsAboutToSteal).ToList();
        }
        return sim;
    }

    public static bool IsAboutToSteal(Creature enemy) =>
        !enemy.IsDead && enemy.Monster is ThievingHopper hopper && hopper.NextMove.StateId == "THIEVERY_MOVE";

    public Sim Clone() => new Sim(Player, Copy(Cards), Copy(Potions))
    {
        OpenPotionSlots = OpenPotionSlots,
        Turn = Turn,
        Thieves = Thieves.ToList(),
        Powers = Powers.Select(p => new PowerSlot(p.Type, p.Amount, p.Key)).ToList(),
    };

    /// <summary>Generator positions, to notice when the real game has rolled since a plan was made.</summary>
    public static (int cards, int potions) Counters(Player player) =>
        (player.RunState.Rng.CombatCardGeneration.ToSerializable().counter,
         player.RunState.Rng.CombatPotionGeneration.ToSerializable().counter);

    private static Rng Copy(Rng rng) => new Rng(rng.ToSerializable());

    public IEnumerable<CardModel> CharacterPool =>
        Player.Character.CardPool.GetUnlockedCards(Player.UnlockState, Player.RunState.CardMultiplayerConstraint);

    public IEnumerable<CardModel> ColorlessPool =>
        ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(Player.UnlockState, Player.RunState.CardMultiplayerConstraint);

    /// <summary>CardFactory.GetDistinctForCombat, without creating the cards.</summary>
    public List<Item> Distinct(IEnumerable<CardModel> pool, int count, bool upgraded = false)
    {
        IEnumerable<CardModel> cards = FilterForPlayerCount(Player.RunState, pool);
        return CardFactory.FilterForCombat(cards).TakeRandom(count, Cards)
            .Select(c => new Item(c, null, upgraded)).ToList();
    }

    /// <summary>CardFactory.GetForCombat (repeats allowed), without creating the cards.</summary>
    public List<Item> Many(IEnumerable<CardModel> pool, int count, bool upgraded = false)
    {
        List<CardModel> options = CardFactory.FilterForCombat(pool).ToList();
        options = FilterForPlayerCount(Player.RunState, options).ToList();
        List<Item> list = new List<Item>();
        for (int i = 0; i < count; i++)
        {
            CardModel? card = Cards.NextItem(options);
            if (card != null)
            {
                list.Add(new Item(card, null, upgraded));
            }
        }
        return list;
    }

    /// <summary>PotionFactory.CreateRandomPotionInCombat (no healing potions).</summary>
    public Item PotionInCombat() => new Item(null, PotionFactory.CreateRandomPotionInCombat(Player, Potions), false);

    /// <summary>PotionFactory.CreateRandomPotionOutOfCombat (any potion).</summary>
    public Item PotionOutOfCombat() => new Item(null, PotionFactory.CreateRandomPotionOutOfCombat(Player, Potions), false);
}
