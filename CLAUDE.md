# Project notes for Claude

> **First thing in a new session: remind the user to make sure the latest build is installed, then inspect it in
> game before doing anything else.** The latest build (power-card previews for Creative AI etc., planned
> cards/potions leaving F8's "Next", the grayed "Not in this fight" section) was installed at the end of the last
> session but not yet looked at in game. If anything changed since, ask them to close the game and run
> `! cd "<this repo>" && dotnet build -c Release`, then go through "Not verified in game yet" below with them.
> Remove this note once they've confirmed.

Slay the Spire 2 (**v0.111.0**) mod: tooltips + an F8 planner showing what random generators will produce next. Part
of testing whether a seed is unwinnable with perfect information; companion to the seed search tool in
`C:\Users\chook\Desktop\Searching for an Unwinnable Seed` (Necrobinder, Act 1, forced early elite) and to the
sibling mod repo **CustomDrawOrder** (expected at `C:\Users\chook\Desktop\CustomDrawOrder`; it lets the player choose
every draw). See README.md for the player's view.

## Ground rules

- **Never guess game behaviour; read the decompiled code.** Decompile into the session scratchpad (never commit it):
  `ilspycmd -p -o <scratch>/sts2src "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll"`
  (~1 min). BaseLib: `...\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll`.
- Predictors in `Effects.cs` are line-by-line copies of each card/potion/relic/power's roll (same pool, same count,
  same factory call). Copy from the decompiled source; don't paraphrase.
- Previews must never touch the real game: roll on copies of the generators (`new Rng(rng.ToSerializable())`) and
  return canonical models. Never call `CardFactory.GetDistinctForCombat`/`GetForCombat` themselves: they call
  `CombatState.CreateCard`, which registers cards in the combat.
- Install fails with "file locked by SlayTheSpire2.exe" while the game runs. Don't kill the game; ask the user to
  close it and run `! cd "<this repo>" && dotnet build -c Release`.
- The user tests in game; things not yet confirmed are under "Not verified in game yet".
- Commit only when asked.

## Build / verify

- `dotnet build -c Release` builds and copies `RngPreview.dll`, `.json` and `RngPreview.BaseLib.dll` (only built if
  BaseLib.dll exists) into `<game>\mods\RngPreview\`. `-p:Install=false` skips installing; `-p:GameDir=...` /
  `-p:BaseLibDll=...` override paths. Mods load only at game start.
- `cd tools\PatchCheck && dotnet run -c Release -- ../../bin/Release/net9.0/RngPreview.dll`: applies the mod's Harmony
  patches to the real sts2.dll without the game, lists every patched method (8 expected) and runs all static
  constructors. A missing line means a patch target didn't match. Don't reference the mod from that project (.NET
  would load the stale copy instead of the path given).
- `cd tools\RngCheck && dotnet run -c Release -- "C:\Users\chook\Desktop\Searching for an Unwinnable Seed\SeedSearch\bin\Release\net9.0\SeedSearch.dll"`:
  boots a real run headlessly through SeedSearch (by reflection; its classes are internal), compiles `Sim.cs` +
  `Effects.cs` in, and checks predictions against the game's own code on seed `AGRTQKUWEH84`. Trick for comparing
  with `GetDistinctForCombat`: its random pick runs eagerly and only the `CreateCard` projection is lazy, so read the
  iterator's `_source` field and `Take(n)` without enumerating it. Headless LocString text is empty, so print
  `Id.Entry`, not titles. Add a check here for every new predictor. Anything needing a live combat (piles, enemies)
  can only be checked in game.

## Mod loading (v0.111.0)

- A mod is `<game>\mods\<Id>\<Id>.json` (manifest) + `<Id>.dll`. `Mod.cs` is the `[ModInitializer]`: load settings,
  `Harmony.PatchAll`, register with BaseLib, hook `SceneTree.ProcessFrame` for the F8 panel.
- `ModToggle/` (namespace `ModToggle`, with `ModInfo.cs`) is the Enabled tickbox: settings file, panel in Settings →
  Mods (`NModInfoContainer.Fill/Clear` patches), and `BaseLibBridge`. **The same code is duplicated in the
  CustomDrawOrder repo**; keep the two copies in sync when changing it.
- The game calls `GetTypes()` on every mod DLL, so nothing in the main DLL may reference BaseLib. The BaseLib part is a
  separate `RngPreview.BaseLib.dll` (`BaseLibCompat/`), loaded by `BaseLibBridge` only when BaseLib is loaded (pattern
  from the user's mod `C:\Users\chook\Desktop\STS2-adjust-unknown-chances`). BaseLib titles the entry from
  `<ROOT NAMESPACE OF THE ModConfig CLASS>.mod_title` in `settings_ui`, so the compat code must stay in namespace
  `RngPreview.*`.
- Settings: `%APPDATA%\SlayTheSpire2\RngPreview.settings.json` (never under mods/: every .json there is read as a
  manifest).
- Custom UI text without a .pck: merge keys into a loc table at use time (`LocManager.Instance.GetTable(...).MergeWith`).
- Mod UI is built from stock Godot nodes in code (mod assemblies can't register node scripts); the game's tickbox is
  an `NTickbox` with `res://scenes/screens/settings_tickbox.tscn`'s children moved in.
- MSBuild gotchas: XML comments can't contain `--`; `tools/` and `BaseLibCompat/` must stay excluded from the mod's
  compile; the game folder ships .NET runtime DLLs, so PatchCheck uses `UseAppHost=false`.
- Custom Draw Order's draw screen is a player choice during turn start, which changes the turn-1 roll order (see Game
  RNG facts); `Effects.DrawScreenOnTurnOne` checks whether the mod with id `CustomDrawOrder` is loaded.

## Game RNG facts

- Run-wide generators (`RunState.Rng`, seeded from the run seed + name, never reset between combats) and users:

  | Generator | Users | Notes |
  |---|---|---|
  | CombatCardGeneration | 33 | new cards; also Thieving Hopper's steal. Covered by RngPreview |
  | CombatPotionGeneration | 5 | Alchemize, Entropic Brew, Delicate Frond, Alchemical Coffer/Phial Holster (pickup). Covered |
  | CombatCardSelection | 16 | picks existing cards: Entropy (transform), True Grit, Drain Power, Seeker Strike, Thrash, random autoplay from draw pile, ... **not covered** |
  | Shuffle | 7 | combat-start shuffle, reshuffles, `CardPilePosition.Random` inserts, Beat Down, Catastrophe, Uproar, Stampede. **not covered** |
  | MonsterAi | all monsters | `MonsterModel` rolls `NextMove = MoveStateMachine.RollMove(targets, Creature, RunRng.MonsterAi)`; also Fabricator, FlutterPower. **not covered** |
  | CombatTargets | 15 | random targets: Bouncing Flask, Juggernaut, Serpent Form, Lightning orb, Haunt, Countdown (Necrobinder), Kusarigama, Tingsha, ... **not covered** |
  | CombatEnergyCosts | 3 | Snecko Oil, Confused, Slither. **not covered** |
  | CombatOrbs | 2 | Chaos, Trash to Treasure. **not covered** |
  | Niche | 18 | almost all out of combat (relic pickups); Tough Egg hatchling HP |

  Per-player `PlayerRng.Rewards` (fight rewards, potion drop odds) is separate: combat generators never affect rewards.
  Each monster also gets `monster.Rng` (seed + map coord + act + combat id; encounter variants). `Rng.Chaotic` is
  cosmetic only (skins, screen shake, dialogue).
- Every `Rng.NextX` call consumes exactly one 64-bit draw (`NextDouble * max`, no rejection sampling), so a
  `NextItem` always advances by one regardless of list size. `TakeRandom(n)` = `UnstableShuffle` of the whole
  filtered pool then `Take(n)`, so it advances by ~pool size regardless of n.
- `GetDistinctForCombat` = `FilterForPlayerCount` (private; called via `AccessTools`) → `FilterForCombat` →
  `TakeRandom`. `GetForCombat` = repeats allowed, one `NextItem` per card.
- Turn start order (`CombatManager.StartTurn`): `Creature.BeforeTurnStart` (sets `AmountOnTurnStart = Amount`) →
  `SetupPlayerTurn` [`BeforeHandDraw` (powers in application order, then relics) → hand draw → `AfterPlayerTurnStart`]
  → `AfterSideTurnStart` (Crossbow, Big Hat, Orange Dough). **But** `SetupPlayerTurn` runs under a
  `HookPlayerChoiceContext`: at the first player choice (Toolbox's screen, Choices Paradox's screen, Custom Draw
  Order's draw screen) the outer sequence continues, so `AfterSideTurnStart` rolls at that point and everything after
  the choice runs later. `Effects.NextCombat` models this.
- Potions leave their slot before taking effect (`RemoveBeforeUse`), so Entropic Brew also refills its own slot.
- Rolling powers are `PowerStackType.Counter`, `InstanceType.None`: re-applying stacks into the existing power, a new
  one is appended to `Creature._powers` (so it rolls after existing powers).
- Thieving Hopper: moves are a fixed cycle starting with `THIEVERY_MOVE` (no MonsterAi roll). The steal happens on
  the enemy turn after the hand flush: candidates = draw pile then discard (deck cards only, `DeckVersion != null`),
  best tier of Uncommon > Common/Rare/Event > Basic/Quest > Ancient/Imbued, one `NextInt`. Turn end: Ethereal
  exhausted, turn-end-effect cards (Burn) to discard, then the rest of the hand except Retain
  (`ShouldRetainThisTurn`, `Hook.ShouldFlush`).
- Hover tips: `CardModel/PotionModel/RelicModel/PowerModel/Creature.HoverTips` getters (computed when the hover
  opens); intents use `AbstractIntent.GetHoverTip` (a single `HoverTip` record struct; `Description` has a private
  setter, set via reflection on the boxed value). `HoverTipFactory.FromCard(card, upgrade)` gives card previews.

## Code structure

- `Sim.cs`: `Sim` (generator copies, open potion slots, turn, thieves, power stacks), `Item`, `Roll`, `PowerSlot`.
- `Effects.cs`: every predictor, `PlayCard`/`UsePotion`/`PreviewPowerCard`, `EndTurn`, `NextCombat`,
  `NextPassiveRoll`.
- `Planner.cs`: options (generator cards in hand/draw/discard grouped by title, potions, Calamity attack, End turn,
  Win combat) and the plan; the plan resets when the live generator counters, turn or combat change. User
  preference: a planned card or potion uses up a copy (`Step.CopiesKey`/`Copies`; row shows "N planned"). Once every
  copy is planned: potions and cards that leave the fight (`Step.LeavesFight`: Power type, or `Keywords` has Exhaust;
  Soul's Power / Discovery+ remove Exhaust) disappear from "Next", and each planned copy of such a card is listed in
  "Not in this fight"; cards that go to the discard pile stay at the bottom of "Next", grayed but clickable
  (`Step.Spent`), since a reshuffle could bring them back. A planned power's roll still shows under End turn, and power
  texts say they give nothing if the combat ends this turn. End turn / Win combat / Calamity attack rows are repeatable.
  "Not in this fight" (grayed, bottom; also a user request): deck generator cards whose combat copy (`DeckVersion`) isn't
  in hand/draw/discard (exhausted / played power / transformed), plus generator cards held by a living Thieving
  Hopper's `SwipePower.StolenCard` (the steal removes the deck card; killing the hopper gives it back as a reward).
  Each is previewed as the first roll of the next combat: after the plan, then `NextCombat` unless the plan already
  has "Win this combat". `NextCombat` clears simulated powers.
- `Panel.cs`: F8 / "RNG (F8)" button overlay. `Tooltips.cs`: hover patches.
- Card class `Calamity` clashes with the method `Effects.Calamity`; fully qualify the card type.

## Next steps (agreed with the user, not started)

1. **F8 tabs per generator**: Card generation (current content), Potions, CombatCardSelection, Shuffle, plus
   **Enemy intents (MonsterAi)** and **Random targets (CombatTargets)**; Energy costs + Orbs could share "Other".
   CombatCardSelection results depend on what's picked from, so rows like "Entropy on Strike → X, on Defend → Y"
   (Entropy: `CardCmd.TransformToRandom(card, CombatCardSelection)` → `CardFactory.CreateRandomCardForTransform` →
   `NextItem(GetDefaultTransformationOptions(card, true))`; check `GetFilteredTransformationOptions`). Targets depend
   on which enemies are alive.
2. **Enemy intents** is the biggest piece: simulate each monster's `MoveStateMachine.RollMove` on a copy without
   mutating the monster, several turns ahead. The user pointed to a Steam Workshop mod that already reads/predicts
   next moves: https://steamcommunity.com/sharedfiles/filedetails/?id=3747528152 . The page was rate-limited (HTTP
   429) in the last session and a web search found no GitHub for it; try again (Workshop page "source"/GitHub link,
   GitHub search for STS2 intent/bestiary mods) and reuse its approach if the licence allows.
3. Planner gaps: cards offered by a generator (e.g. Discovery offering Creative AI) can't be added to the plan; card
   replays (play twice) need two clicks; Stoke uses the current hand size; turn-1 order ignores hand-size relics.

## Not verified in game yet

Ask the user to check after installing: the F8 panel layout and that its clicks don't reach the cards underneath;
hover tooltips (cards, potions, relics, powers, power cards, Thieving Hopper body and intent); the Enabled tickboxes in
Settings → Mods and BaseLib's Mod Configuration; power-card previews (Creative AI etc.); planned cards/potions leaving
"Next"; the grayed "Not in this fight" section (exhaust a Jack of All Trades, play Creative AI, let a Thieving Hopper
steal a generator); planning Spectrum Shift moves it to "Not in this fight"; planning every copy of a non-Exhaust
generator (Discovery+, Soul's Power) leaves it grayed but clickable at the bottom of "Next".
