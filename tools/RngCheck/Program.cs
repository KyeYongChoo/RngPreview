using System.Reflection;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using RngPreview;

var ss = Assembly.LoadFrom(args[0]);
ss.GetType("Headless")!.GetMethod("Boot")!.Invoke(null, null);
var run = (RunState)ss.GetType("Sim")!.GetMethod("NewRun")!.Invoke(null, new object[] { "AGRTQKUWEH84", true, false })!;
var p = run.Players[0];
foreach (var r in new RelicModel[] { ModelDb.Relic<Toolbox>(), ModelDb.Relic<BigHat>(), ModelDb.Relic<Crossbow>(), ModelDb.Relic<ChoicesParadox>(),
             ModelDb.Relic<OrangeDough>(), ModelDb.Relic<VexingPuzzlebox>(), ModelDb.Relic<DelicateFrond>() })
    p.AddRelicInternal(r.ToMutable(), -1, true);

string Show(IEnumerable<Roll> rolls) => string.Join(" | ", rolls.Select(r =>
    $"{r.Source?.GetType().Name ?? r.Label}: " + string.Join(", ", r.Items.Select(i => (i.Card?.Id.Entry ?? i.Potion!.Id.Entry) + (i.ShowUpgraded ? "+" : "")))));
var before = Sim.Counters(p);
int fails = 0;
void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

var cards = new CardModel[] { ModelDb.Card<Abundance>(), ModelDb.Card<Alchemize>(), ModelDb.Card<BundleOfJoy>(), ModelDb.Card<Discovery>(),
    ModelDb.Card<Distraction>(), ModelDb.Card<InfernalBlade>(), ModelDb.Card<JackOfAllTrades>(), ModelDb.Card<Jackpot>(), ModelDb.Card<Largesse>(),
    ModelDb.Card<ManifestAuthority>(), ModelDb.Card<Metamorphosis>(), ModelDb.Card<Quasar>(), ModelDb.Card<Splash>(), ModelDb.Card<WhiteNoise>() }
    .Select(c => (CardModel)c.ToMutable()).ToList();
var potions = new PotionModel[] { ModelDb.Potion<AttackPotion>(), ModelDb.Potion<ColorlessPotion>(), ModelDb.Potion<CosmicConcoction>(),
    ModelDb.Potion<EntropicBrew>(), ModelDb.Potion<OrobicAcid>(), ModelDb.Potion<PowerPotion>(), ModelDb.Potion<SkillPotion>() }
    .Select(x => x.ToMutable()).ToList();

foreach (var c in cards) { var a = Show(Effects.PlayCard(Sim.Live(p), c)); var b = Show(Effects.PlayCard(Sim.Live(p), c)); Console.WriteLine("  " + a); if (a != b) Check(c.Id.Entry + " deterministic", false); }
foreach (var x in potions) { var a = Show(Effects.UsePotion(Sim.Live(p), x)); Console.WriteLine("  " + a); }
Console.WriteLine("  NEXT COMBAT: " + Show(Effects.NextCombat(Sim.Live(p))));
Check("predictions never move the real generators", Sim.Counters(p) == before);

// Real game code vs prediction. Discovery: GetDistinctForCombat rolls eagerly; read its picks without creating cards.
var discovery = cards.First(c => c is Discovery);
var predicted = Effects.PlayCard(Sim.Live(p), discovery)[0].Items.Select(i => i.Card!.Id.Entry).ToList();
var real = CardFactory.GetDistinctForCombat(p, p.Character.CardPool.GetUnlockedCards(p.UnlockState, p.RunState.CardMultiplayerConstraint), 3, p.RunState.Rng.CombatCardGeneration);
var src = (IEnumerable<CardModel>)real.GetType().GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(real)!;
var actual = src.Take(3).Select(c => c.Id.Entry).ToList();
Console.WriteLine($"  Discovery predicted {string.Join(",", predicted)} / game {string.Join(",", actual)}");
Check("Discovery prediction matches the game's GetDistinctForCombat", predicted.SequenceEqual(actual));
// The next prediction must now match the game's next roll (the real generator moved on).
var predicted2 = Effects.PlayCard(Sim.Live(p), discovery)[0].Items.Select(i => i.Card!.Id.Entry).ToList();
var real2 = CardFactory.GetDistinctForCombat(p, p.Character.CardPool.GetUnlockedCards(p.UnlockState, p.RunState.CardMultiplayerConstraint), 3, p.RunState.Rng.CombatCardGeneration);
var actual2 = ((IEnumerable<CardModel>)real2.GetType().GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(real2)!).Take(3).Select(c => c.Id.Entry).ToList();
Check("second Discovery prediction matches the game's second roll", predicted2.SequenceEqual(actual2) && !predicted2.SequenceEqual(predicted));
// Alchemize uses the game's own potion function directly.
var alch = cards.First(c => c is Alchemize);
var pp = Effects.PlayCard(Sim.Live(p), alch)[0].Items[0].Potion!.Id.Entry;
var rp = PotionFactory.CreateRandomPotionInCombat(p, p.RunState.Rng.CombatPotionGeneration).Id.Entry;
Check($"Alchemize prediction {pp} matches the game's roll {rp}", pp == rp);

// Power cards: Creative AI's next-turn roll is one Power card from the character pool, like White Noise.
var beforePowers = Sim.Counters(p);
var creative = (CardModel)ModelDb.Card<CreativeAi>().ToMutable();
var whiteNoise = cards.First(c => c is WhiteNoise);
var cai = Effects.PreviewPowerCard(Sim.Live(p), creative);
var wn = Effects.PlayCard(Sim.Live(p), whiteNoise)[0].Items.Select(i => i.Card!.Id.Entry).ToList();
Console.WriteLine("  Creative AI preview: " + Show(cai) + "   White Noise: " + string.Join(",", wn));
Check("Creative AI preview = next Power card roll", cai.Count == 1 && cai[0].Items.Select(i => i.Card!.Id.Entry).SequenceEqual(wn));
// Played twice in a plan: one stack of 2, which rolls twice at turn start; Crossbow (relic) rolls after it.
var sim = Sim.Live(p);
Effects.PlayCard(sim, creative); Effects.PlayCard(sim, creative);
var turn = Effects.EndTurn(sim);
Console.WriteLine("  after 2x Creative AI, end turn: " + Show(turn));
Check("two Creative AI plays stack into 2 rolls, before Crossbow", turn.Count == 2 && turn[0].Label == "Creative AI" && turn[0].Items.Count == 2 && turn[1].Source is Crossbow);
// Spectrum Shift played after Creative AI rolls after it (new powers go last).
var sim2 = Sim.Live(p);
Effects.PlayCard(sim2, creative); Effects.PlayCard(sim2, (CardModel)ModelDb.Card<SpectrumShift>().ToMutable());
var turn2 = Effects.EndTurn(sim2);
Check("powers roll in the order they were applied", turn2.Select(r => r.Label).SequenceEqual(new[] { "Creative AI", "Spectrum Shift", turn2[2].Label }) && turn2[2].Source is Crossbow);
// Calamity card: the next Attack's roll is one Attack from the character pool (repeats allowed).
var cal = Effects.PreviewPowerCard(Sim.Live(p), (CardModel)ModelDb.Card<Calamity>().ToMutable());
var calSim = Sim.Live(p); Effects.PlayCard(calSim, (CardModel)ModelDb.Card<Calamity>().ToMutable());
var direct = Effects.CalamityRoll(calSim, Effects.Calamity(calSim)!);
Console.WriteLine("  Calamity preview: " + Show(cal));
Check("Calamity card preview = Calamity's roll", Show(cal).Split(':')[1] == Show(new[] { direct }).Split(':')[1]);
Check("power card previews never move the real generators", Sim.Counters(p) == beforePowers);

// Next combat: powers don't carry over, and a card's next-combat roll comes after the turn-1 relics.
var carry = Sim.Live(p);
Effects.PlayCard(carry, creative);
Effects.NextCombat(carry);
Check("powers are cleared when the next combat starts", carry.Powers.Count == 0);
var start = Sim.Live(p);
var startRolls = Effects.NextCombat(start);
var nextCai = Effects.PreviewPowerCard(start.Clone(), creative);
var nextWn = Effects.PlayCard(start.Clone(), whiteNoise)[0].Items.Select(i => i.Card!.Id.Entry).ToList();
Console.WriteLine("  next combat: " + Show(startRolls) + "  then Creative AI: " + Show(nextCai));
Check("next-combat Creative AI preview = next Power roll after the turn-1 relics",
    nextCai.Count == 1 && nextCai[0].Items.Select(i => i.Card!.Id.Entry).SequenceEqual(nextWn) && !nextWn.SequenceEqual(wn));
Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
