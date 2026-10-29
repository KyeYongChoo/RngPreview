# RNG Preview (Slay the Spire 2 mod)

Shows what every random card and potion generator will give before you use it, and plans the order to use them in.
Made for testing whether a seed is unwinnable: if a fight can't be won even knowing every roll in advance, no luck
would have saved it.

Works well with its sibling mod **Custom Draw Order**, which lets you choose every card you draw.

> Not affiliated with Mega Crit. Built against Slay the Spire 2 **v0.111.0**.

## How the game rolls

All random card and potion generation in combat comes from two generators that run for the whole run:

- **Combat card generation**: Discovery, Splash, Attack/Skill/Power/Colorless Potion, Jack of All Trades,
  Infernal Blade, Toolbox, Crossbow, Creative AI, ... (34 users in all, including Thieving Hopper's steal).
- **Combat potion generation**: Alchemize, Entropic Brew, Delicate Frond (and Alchemical Coffer / Phial Holster when
  picked up).

They're seeded from the run seed and never reset, so the results depend only on how many rolls came before. They're
separate from rewards: using these effects never changes card or potion rewards.

## What it does

- **Hover** a card in your hand, draw pile or discard pile, a potion, a relic or a power, and its tooltip shows what it
  will generate next, with card previews:
  - cards and potions: if you play or use it next;
  - powers (Creative AI, Hello World, Call of the Void, Spectrum Shift): at the start of next turn; Calamity: on your
    next Attack;
  - the cards that give those powers, before you play them: what the power will generate at the start of next turn
    if you play the card and end the turn (Calamity: on the Attack after it). Playing another copy adds to the
    existing stack; a new power rolls after the ones you already have;
  - Crossbow: next turn; Toolbox, Big Hat, Orange Dough, Choices Paradox, Vexing Puzzlebox, Delicate Frond: at the
    start of the next combat (also works on the map);
  - Thieving Hopper (hover it or its intent): the card it will steal on its turn. It steals a random deck card (never
    a generated one) from your draw and discard piles, preferring Uncommon, then Common/Rare, then Basic, then
    Ancient/Imbued. The preview counts your hand as discarded at the end of the turn (except Retain and Ethereal
    cards), so it changes as you play cards.
- **F8** (or the **RNG (F8)** button in combat) opens the planner. Each row is something that rolls (each generator
  card in your hand, draw or discard pile, each generator potion, playing an Attack with Calamity, ending the turn,
  winning the combat) with exactly what it gives if it's the next thing to roll. Click a row to add it to the plan;
  every other row updates as if it came after. Undo / Clear plan to try another order.
  - Each planned card or potion uses up a copy. Once every copy is planned, potions, Power cards and cards that
    Exhaust leave "Next" (the cards move to "Not in this fight"). Other cards (e.g. Discovery+, or Jack of All Trades
    with Soul's Power) go to your discard pile and could be reshuffled and played again, so they stay at the bottom
    of "Next", grayed out but still clickable.
  - **Power cards** (Creative AI, Hello World, Call of the Void, Spectrum Shift, Calamity) show what their power will
    generate next turn, and that they give nothing if the combat ends this turn. Adding one to the plan rolls nothing
    yet; it adds the power, so a later **End turn** (or Attack, for Calamity) in the plan includes it.
  - **End turn** runs the enemy turn (the card Thieving Hopper steals) and the next turn's start: powers in the
    order you got them, then Crossbow.
  - **Win this combat** runs the next combat's start: Delicate Frond fills your empty slots, then the turn 1 relics in
    the game's order. That order changes when the turn start stops for a choice (Toolbox's or Choices Paradox's screen,
    or Custom Draw Order's draw screen): Big Hat, Orange Dough and Crossbow roll at that moment instead of last.
  - **Not in this fight** (grayed, at the bottom): generator cards from your deck you can't use this fight
    (exhausted, played as a power, transformed, or planned and about to be one of those), and ones a Thieving Hopper
    stole (you only get those back by killing it). Each shows what it gives if it's the first thing to roll next
    combat, after your plan and the next combat's start.
  - The plan resets as soon as the game rolls for real, or the turn or combat changes.

Every prediction is the game's own selection code run on a copy of the generator, so it never changes what you'll
really get.

## Turning it on and off

An **Enabled** tickbox takes effect immediately (no restart). It's in two places, which change the same setting:

- **Main Menu → Settings → Mods → RNG Preview** (always available)
- **Main Menu → Mod Configuration → RNG Preview** (only when
  [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) is installed; BaseLib is optional)

The setting is saved to `%APPDATA%\SlayTheSpire2\RngPreview.settings.json`.

## Install

Needs the [.NET 9 SDK](https://dotnet.microsoft.com/download). From this folder:

```
dotnet build -c Release
```

The build copies `RngPreview.dll`, `RngPreview.BaseLib.dll` and `RngPreview.json` into the game's `mods\RngPreview\`
folder. Restart the game afterwards (mods only load at startup); close it first, or the copy fails because the game
has the files open.

- The BaseLib DLL is only built when BaseLib is found at its Workshop path
  (`steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll`; override with `-p:BaseLibDll="..."`). It's a
  separate file because the game loads every type in a mod's DLL at startup, and a BaseLib reference would stop the
  mod loading for players without BaseLib.
- Game installed elsewhere: `-p:GameDir="D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"`
- Build without installing: `-p:Install=false`

## Limits

- Predictions are exact for the current state. They assume nothing else rolls first; the planner is how you account
  for things that will.
- The planner doesn't know about effects that change while you plan: cards you'd generate and then play, powers you'd
  gain, potion slots you'd fill or empty some other way, or a card played twice by a replay effect (add it twice).
- Stoke generates one card per card in your hand when you play it; the preview uses your current hand.
- Not covered yet: the other generators that roll in combat, such as enemy intents, random targets, random card
  picks (Entropy's transforms, True Grit's random exhaust), shuffles and random card costs.
- The turn 1 order assumes Custom Draw Order shows its draw screen whenever more cards are free to choose than you'll
  draw, and ignores relics that change the hand size.
- Covers v0.111.0's generators (`Effects.cs`, each copied from the game's code). A game update can add or change them.

## Files

- `Sim.cs`, `Effects.cs`: the generator copies and every predictor.
- `Planner.cs`, `Panel.cs`: the F8 planner. `Tooltips.cs`: the hover previews.
- `ModToggle/`, `ModInfo.cs`, `BaseLibCompat/`: the Enabled tickbox and its two settings pages.
- `tools/PatchCheck/`, `tools/RngCheck/`: checks against the game without starting it.
