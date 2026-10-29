using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Modding;

namespace RngPreview;

/// <summary>
/// Every user of the CombatCardGeneration / CombatPotionGeneration generators in v0.111.0, each rolling exactly as
/// its OnPlay / OnUse / hook does (same pool, same count, same call). Keep in sync with the game after updates.
/// </summary>
internal static class Effects
{
    private static int CardsVar(AbstractModel model) => model switch
    {
        CardModel c => c.DynamicVars.Cards.IntValue,
        PotionModel p => p.DynamicVars.Cards.IntValue,
        RelicModel r => r.DynamicVars.Cards.IntValue,
        _ => 0,
    };

    private static readonly Dictionary<Type, Func<Sim, CardModel, List<Item>>> CardEffects = new()
    {
        [typeof(Abundance)] = (s, c) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Power), 3, upgraded: true),
        [typeof(Alchemize)] = (s, c) =>
        {
            Item potion = s.PotionInCombat();
            if (s.OpenPotionSlots > 0)
            {
                s.OpenPotionSlots--;
            }
            return new List<Item> { potion };
        },
        [typeof(BundleOfJoy)] = (s, c) => s.Distinct(s.ColorlessPool, CardsVar(c)),
        [typeof(Discovery)] = (s, c) => s.Distinct(s.CharacterPool, 3),
        [typeof(Distraction)] = (s, c) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Skill), 1),
        [typeof(InfernalBlade)] = (s, c) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Attack), 1),
        [typeof(JackOfAllTrades)] = (s, c) => s.Distinct(s.ColorlessPool.Where(x => x is not JackOfAllTrades), CardsVar(c)),
        [typeof(Jackpot)] = (s, c) => s.Many(s.CharacterPool.Where(x =>
            x.EnergyCost != null && x.EnergyCost.Canonical == 0 && !x.EnergyCost.CostsX), CardsVar(c), c.IsUpgraded),
        [typeof(Largesse)] = (s, c) => s.Distinct(s.ColorlessPool, 1, c.IsUpgraded),
        [typeof(MadScience)] = (s, c) => ((MadScience)c).TinkerTimeRider == TinkerTime.RiderEffect.Chaos
            ? s.Distinct(s.CharacterPool, 1)
            : new List<Item>(),
        [typeof(ManifestAuthority)] = (s, c) => s.Distinct(s.ColorlessPool, 1, c.IsUpgraded),
        [typeof(Metamorphosis)] = (s, c) => s.Many(s.CharacterPool.Where(x => x.Type == CardType.Attack), CardsVar(c)),
        [typeof(Quasar)] = (s, c) => s.Distinct(s.ColorlessPool, 3, c.IsUpgraded),
        [typeof(Splash)] = (s, c) =>
        {
            List<CardPoolModel> pools = s.Player.UnlockState.CharacterCardPools.ToList();
            if (pools.Count > 1)
            {
                pools.Remove(s.Player.Character.CardPool);
            }
            IEnumerable<CardModel> attacks = pools
                .SelectMany(p => p.GetUnlockedCards(s.Player.UnlockState, s.Player.RunState.CardMultiplayerConstraint))
                .Where(x => x.Type == CardType.Attack);
            return s.Distinct(attacks, 3, c.IsUpgraded);
        },
        // Stoke exhausts your hand and generates that many cards; the hand it sees is your current hand without it.
        [typeof(Stoke)] = (s, c) => s.Many(s.CharacterPool,
            PileType.Hand.GetPile(s.Player).Cards.Count(x => x != c), c.IsUpgraded),
        [typeof(WhiteNoise)] = (s, c) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Power), 1),
    };

    private static readonly Dictionary<Type, Func<Sim, PotionModel, List<Item>>> PotionEffects = new()
    {
        [typeof(AttackPotion)] = (s, p) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Attack), 3),
        [typeof(ColorlessPotion)] = (s, p) => s.Distinct(s.ColorlessPool, 3),
        [typeof(CosmicConcoction)] = (s, p) => s.Distinct(s.ColorlessPool, CardsVar(p), upgraded: true),
        [typeof(EntropicBrew)] = (s, p) =>
        {
            List<Item> list = new List<Item>();
            while (s.OpenPotionSlots > 0)
            {
                list.Add(s.PotionOutOfCombat());
                s.OpenPotionSlots--;
            }
            return list;
        },
        [typeof(OrobicAcid)] = (s, p) =>
            s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Attack), 1)
                .Concat(s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Skill), 1))
                .Concat(s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Power), 1)).ToList(),
        [typeof(PowerPotion)] = (s, p) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Power), 3),
        [typeof(SkillPotion)] = (s, p) => s.Distinct(s.CharacterPool.Where(x => x.Type == CardType.Skill), 3),
    };

    /// <summary>Powers that roll: at the start of each turn, or (Calamity) after each Attack.</summary>
    public static readonly HashSet<Type> RollingPowers = new()
    {
        typeof(CallOfTheVoidPower), typeof(CreativeAiPower), typeof(HelloWorldPower), typeof(SpectrumShiftPower), typeof(CalamityPower),
    };

    /// <summary>Cards that apply a rolling power: the power and how many stacks (as each card's OnPlay applies it).</summary>
    private static readonly Dictionary<Type, (Type power, Func<CardModel, int> amount)> PowerCards = new()
    {
        [typeof(CallOfTheVoid)] = (typeof(CallOfTheVoidPower), c => (int)c.DynamicVars.Cards.BaseValue),
        [typeof(CreativeAi)] = (typeof(CreativeAiPower), c => (int)c.DynamicVars["CreativeAi"].BaseValue),
        [typeof(HelloWorld)] = (typeof(HelloWorldPower), c => 1),
        [typeof(SpectrumShift)] = (typeof(SpectrumShiftPower), c => (int)c.DynamicVars.Cards.BaseValue),
        [typeof(MegaCrit.Sts2.Core.Models.Cards.Calamity)] = (typeof(CalamityPower), c => 1),
    };

    public static bool IsPowerCard(CardModel card) => PowerCards.ContainsKey(card.GetType());

    public static bool IsGenerator(CardModel card) =>
        IsPowerCard(card) ||
        CardEffects.ContainsKey(card.GetType()) && (card is not MadScience ms || ms.TinkerTimeRider == TinkerTime.RiderEffect.Chaos);

    public static bool IsGenerator(PotionModel potion) => PotionEffects.ContainsKey(potion.GetType());

    public static PowerSlot? Calamity(Sim s) => s.Powers.FirstOrDefault(p => p.Type == typeof(CalamityPower));

    /// <summary>
    /// Playing <paramref name="card"/>: its own roll, then Calamity's if it is an Attack. A power card rolls nothing
    /// now; it adds to the power's stack (or puts a new power last), which rolls later.
    /// </summary>
    public static List<Roll> PlayCard(Sim s, CardModel card)
    {
        if (PowerCards.TryGetValue(card.GetType(), out var power))
        {
            PowerSlot slot = ApplyPower(s, power.power, power.amount(card), card);
            string when = power.power == typeof(CalamityPower)
                ? "rolls after each Attack"
                : "rolls at the start of next turn; nothing if the combat ends this turn";
            return new List<Roll> { new Roll(card, card.Title, Array.Empty<Item>(), $"{PowerName(slot.Type)} {slot.Amount}, {when}") };
        }
        List<Roll> rolls = new List<Roll> { new Roll(card, card.Title, CardEffects[card.GetType()](s, card)) };
        if (card.Type == CardType.Attack && Calamity(s) is { } calamity)
        {
            rolls.Add(CalamityRoll(s, calamity));
        }
        return rolls;
    }

    /// <summary>
    /// What a power card will generate if played now: Calamity's roll on the next Attack, or the power's roll at the
    /// start of next turn if the turn ends right after (including Thieving Hopper and the powers that roll before it).
    /// </summary>
    public static List<Roll> PreviewPowerCard(Sim s, CardModel card,
        string when = "start of next turn, nothing if the combat ends this turn")
    {
        (Type power, Func<CardModel, int> amount) = PowerCards[card.GetType()];
        PowerSlot slot = ApplyPower(s, power, amount(card), card);
        if (power == typeof(CalamityPower))
        {
            return new List<Roll> { CalamityRoll(s, slot) with { Note = "on your next Attack" } };
        }
        return EndTurn(s).Where(r => ReferenceEquals(r.Source, slot.Key))
            .Select(r => r with
            {
                Note = when + (r.Note == null ? "" : "; " + r.Note),
            }).ToList();
    }

    /// <summary>PowerCmd.Apply for these powers: they stack into an existing instance, otherwise go last.</summary>
    private static PowerSlot ApplyPower(Sim s, Type power, int amount, CardModel card)
    {
        PowerSlot? slot = s.Powers.FirstOrDefault(p => p.Type == power);
        if (slot == null)
        {
            slot = new PowerSlot(power, 0, card);
            s.Powers.Add(slot);
        }
        slot.Amount += amount;
        return slot;
    }

    private static string PowerName(Type power) => power.Name switch
    {
        nameof(CallOfTheVoidPower) => "Call of the Void",
        nameof(CreativeAiPower) => "Creative AI",
        nameof(HelloWorldPower) => "Hello World",
        nameof(SpectrumShiftPower) => "Spectrum Shift",
        nameof(CalamityPower) => "Calamity",
        _ => power.Name,
    };

    /// <summary>Using <paramref name="potion"/>; the potion leaves its slot before it takes effect.</summary>
    public static List<Roll> UsePotion(Sim s, PotionModel potion)
    {
        s.OpenPotionSlots++;
        return new List<Roll> { new Roll(potion, potion.Title.GetFormattedText(), PotionEffects[potion.GetType()](s, potion)) };
    }

    public static Roll CalamityRoll(Sim s, PowerSlot calamity) =>
        new Roll(calamity.Key, "Calamity", s.Many(s.CharacterPool.Where(x => x.Type == CardType.Attack), calamity.Amount));

    /// <summary>
    /// End the turn: the enemy turn (Thieving Hopper's steal), then the start of the next player turn. Powers roll in
    /// BeforeHandDraw (in the order they were applied), then Crossbow in AfterSideTurnStart.
    /// </summary>
    public static List<Roll> EndTurn(Sim s)
    {
        List<Roll> rolls = new List<Roll>();
        List<CardModel> stolen = new List<CardModel>();
        foreach (Creature thief in s.Thieves)
        {
            CardModel? card = Steal(s, stolen);
            if (card != null)
            {
                stolen.Add(card);
                rolls.Add(new Roll(thief, "Thieving Hopper", new[] { Item.Existing(card) }, "steals it, if you end the turn with this hand"));
            }
        }
        s.Thieves.Clear();
        s.Turn++;
        foreach (PowerSlot power in s.Powers)
        {
            if (PowerTurnStart(s, power) is { } roll)
            {
                rolls.Add(roll);
            }
        }
        foreach (RelicModel relic in ActiveRelics(s))
        {
            if (relic is Crossbow && CrossbowRoll(s, relic) is { } roll)
            {
                rolls.Add(roll);
            }
        }
        return rolls;
    }

    /// <summary>ThievingHopper.ThieveryMove's priority tiers, best first (from the game's _stealPriorities).</summary>
    private static readonly Func<CardModel, bool>[] StealPriorities =
    {
        c => c.Enchantment is not Imbued && c.Rarity == CardRarity.Uncommon,
        c => c.Enchantment is not Imbued && c.Rarity is CardRarity.Common or CardRarity.Rare or CardRarity.Event,
        c => c.Enchantment is not Imbued && c.Rarity is CardRarity.Basic or CardRarity.Quest,
        c => c.Rarity == CardRarity.Ancient || c.Enchantment is Imbued,
    };

    /// <summary>
    /// The card a Thieving Hopper steals on the enemy turn: a random deck card (not a generated one) from the draw
    /// pile then the discard pile, from the best priority tier present. The discard pile is taken as it will be after
    /// the turn ends with the current hand.
    /// </summary>
    private static CardModel? Steal(Sim s, List<CardModel> alreadyStolen)
    {
        List<CardModel> list = PileType.Draw.GetPile(s.Player).Cards.Concat(DiscardAfterTurnEnd(s.Player))
            .Where(c => c.DeckVersion != null && !alreadyStolen.Contains(c)).ToList();
        IReadOnlyList<CardModel> pick = list;
        foreach (Func<CardModel, bool> tier in StealPriorities)
        {
            List<CardModel> matching = list.Where(tier).ToList();
            if (matching.Count > 0)
            {
                pick = matching;
                break;
            }
        }
        return pick.Count == 0 ? null : pick[s.Cards.NextInt(0, pick.Count)];
    }

    /// <summary>
    /// CombatManager's turn end: Ethereal cards are exhausted, cards with a turn-end effect (Burn, ...) resolve and go
    /// to the discard pile, then the rest of the hand is discarded except Retain cards.
    /// </summary>
    private static IEnumerable<CardModel> DiscardAfterTurnEnd(Player player)
    {
        CombatState state = CombatManager.Instance.DebugOnlyGetState()!;
        List<CardModel> discard = PileType.Discard.GetPile(player).Cards.ToList();
        List<CardModel> hand = PileType.Hand.GetPile(player).Cards.ToList();
        List<CardModel> turnEnd = hand.Where(c => c.HasTurnEndInHandEffect).ToList();
        List<CardModel> ethereal = hand.Where(c => !c.HasTurnEndInHandEffect && c.Keywords.Contains(CardKeyword.Ethereal) &&
                                                   Hook.ShouldEtherealTrigger(state, c)).ToList();
        discard.AddRange(turnEnd.Where(c => !c.Keywords.Contains(CardKeyword.Ethereal)));
        if (Hook.ShouldFlush(state, player))
        {
            discard.AddRange(hand.Except(turnEnd).Except(ethereal).Where(c => !c.ShouldRetainThisTurn));
        }
        return discard;
    }

    private static Roll? PowerTurnStart(Sim s, PowerSlot power)
    {
        switch (power.Type.Name)
        {
            case nameof(CallOfTheVoidPower):
            {
                List<CardModel> pool = s.CharacterPool
                    .Where(c => c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient).ToList();
                if (pool.Count == 0)
                {
                    return null;
                }
                List<Item> items = new List<Item>();
                for (int i = 0; i < power.Amount; i++)
                {
                    items.AddRange(s.Distinct(pool, 1));
                }
                return new Roll(power.Key, "Call of the Void", items, "Ethereal");
            }
            case nameof(CreativeAiPower):
            {
                List<Item> items = new List<Item>();
                for (int i = 0; i < power.Amount; i++)
                {
                    items.AddRange(s.Distinct(s.CharacterPool.Where(c => c.Type == CardType.Power), 1));
                }
                return new Roll(power.Key, "Creative AI", items);
            }
            // Hello World uses AmountOnTurnStart, which is set to Amount when the turn starts.
            case nameof(HelloWorldPower) when power.Amount >= 1:
                return new Roll(power.Key, "Hello World",
                    s.Distinct(s.CharacterPool.Where(c => c.Rarity == CardRarity.Common), power.Amount));
            case nameof(SpectrumShiftPower):
                return new Roll(power.Key, "Spectrum Shift", s.Distinct(s.ColorlessPool, power.Amount));
            default:
                return null;
        }
    }

    private static Roll? CrossbowRoll(Sim s, RelicModel relic)
    {
        List<CardModel> attacks = s.CharacterPool.Where(c => c.Type == CardType.Attack).ToList();
        return attacks.Count == 0 ? null : new Roll(relic, relic.Title.GetFormattedText(), s.Distinct(attacks, 1), "free this turn");
    }

    private static IEnumerable<RelicModel> ActiveRelics(Sim s) => s.Player.Relics.Where(r => !r.IsMelted).ToList();

    /// <summary>
    /// Win this combat and start the next one. Combat start: Delicate Frond fills empty potion slots. Turn 1 runs
    /// BeforeHandDraw (Toolbox), the hand draw, then AfterPlayerTurnStart (Choices Paradox, Vexing Puzzlebox); the
    /// AfterSideTurnStart relics (Big Hat, Orange Dough, Crossbow) run after that, or as soon as the turn start first
    /// stops for a player choice (Toolbox's or Choices Paradox's screen, or Custom Draw Order's draw screen).
    /// </summary>
    public static List<Roll> NextCombat(Sim s)
    {
        List<Roll> rolls = new List<Roll>();
        List<RelicModel> relics = ActiveRelics(s).ToList();
        foreach (RelicModel relic in relics.Where(r => r is DelicateFrond))
        {
            List<Item> potions = new List<Item>();
            while (s.OpenPotionSlots > 0)
            {
                potions.Add(s.PotionOutOfCombat());
                s.OpenPotionSlots--;
            }
            rolls.Add(new Roll(relic, relic.Title.GetFormattedText(), potions));
        }
        s.Turn = 1;
        s.Thieves.Clear();
        s.Powers.Clear(); // powers don't carry over between combats

        bool paused = false;
        void Pause()
        {
            if (paused)
            {
                return;
            }
            paused = true;
            foreach (RelicModel relic in relics)
            {
                Roll? roll = relic switch
                {
                    BigHat => BigHatRoll(s, relic),
                    OrangeDough => new Roll(relic, relic.Title.GetFormattedText(), s.Distinct(s.ColorlessPool, CardsVar(relic))),
                    Crossbow => CrossbowRoll(s, relic),
                    _ => null,
                };
                if (roll != null)
                {
                    rolls.Add(roll);
                }
            }
        }

        foreach (RelicModel relic in relics.Where(r => r is Toolbox))
        {
            rolls.Add(new Roll(relic, relic.Title.GetFormattedText(), s.Distinct(s.ColorlessPool, CardsVar(relic)), "choose 1"));
            Pause();
        }
        if (DrawScreenOnTurnOne(s))
        {
            Pause();
        }
        foreach (RelicModel relic in relics)
        {
            if (relic is ChoicesParadox)
            {
                List<Item> items = s.Distinct(s.CharacterPool, CardsVar(relic));
                rolls.Add(new Roll(relic, relic.Title.GetFormattedText(), items, "choose 1, Retain"));
                if (items.Count > 0)
                {
                    Pause();
                }
            }
            else if (relic is VexingPuzzlebox)
            {
                rolls.Add(new Roll(relic, relic.Title.GetFormattedText(), s.Distinct(s.CharacterPool, 1), "free this turn"));
            }
        }
        Pause();
        return rolls;
    }

    private static Roll? BigHatRoll(Sim s, RelicModel relic)
    {
        List<CardModel> ethereal = s.CharacterPool.Where(c => c.Keywords.Contains(CardKeyword.Ethereal)).ToList();
        return ethereal.Count == 0 ? null : new Roll(relic, relic.Title.GetFormattedText(), s.Distinct(ethereal, CardsVar(relic)));
    }

    /// <summary>
    /// Whether Custom Draw Order will open its draw screen during the turn 1 hand draw: it does when more cards are
    /// free to choose than there are draws left after Innate cards. Ignores relics that change the hand size.
    /// </summary>
    private static bool DrawScreenOnTurnOne(Sim s)
    {
        if (!ModManager.Mods.Any(m => m.manifest?.id == "CustomDrawOrder" && m.state == ModLoadState.Loaded))
        {
            return false;
        }
        IReadOnlyList<CardModel> deck = s.Player.Deck.Cards;
        int innate = deck.Count(c => c.Keywords.Contains(CardKeyword.Innate));
        int bottom = deck.Count(c => c.Enchantment?.ShouldStartAtBottomOfDrawPile ?? false);
        int wanted = Math.Max(5, innate) - innate;
        return wanted > 0 && deck.Count - innate - bottom > wanted;
    }

    /// <summary>The roll <paramref name="source"/> (a relic or power) will make next, or null if it doesn't roll.</summary>
    public static Roll? NextPassiveRoll(Sim s, AbstractModel source)
    {
        bool inCombat = s.Turn > 0;
        bool turnOneRelic = source is Toolbox or BigHat or OrangeDough or ChoicesParadox or VexingPuzzlebox or DelicateFrond;
        List<Roll> rolls = inCombat && !turnOneRelic ? EndTurn(s) : NextCombat(s);
        return rolls.FirstOrDefault(r => ReferenceEquals(r.Source, source));
    }
}
