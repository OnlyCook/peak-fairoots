<!-- GENERATED FILE — do not edit by hand.
     Source: packaging/README.md + packaging/README.github-extra.md
     Regenerate with: bash packaging/gen-readme.sh -->

**Adds balancing, Quality of Life features, and some new mechanics to the Roots biome.**

Fully configurable through **4 presets + a Custom slate**, from light balancing to a pretty much peaceful climb, and every dial also works in the other direction if you'd rather make Roots harder instead.

---

## Highlights

- **Cover your mouth**: press `X` to hold your breath while standing in a spore area.
- **Throw items at creatures**: spiders can already be bonked in vanilla, Fairoots extends the same counterplay to zombies and beetles too, throw something with enough force and knock them out for a while.
- **Spore bombs redone**: vanilla's trigger radius and height are much larger than the prop itself, which this mod fixes while also removing all spore bombs that spawned inside grass or bush props.
- **Climbing counters wind**: never make wind push you off an edge while climbing again.

Every randomized decision Fairoots makes (which spore bombs get removed, for example) is bound to your run's seed: same seed + same Roots level = same result, every time. Not more randomness stacked on top of randomness.

## What else it does

Beyond the highlights above, this mod also softens (or, if you configure it that way, sharpens) wind knockback and duration, zombie/beetle speed and aggression, spore cloud size and *Spore* status clear time, and more.

It also offers a bunch of client-sided features: make spore bombs a darker red to spot them easier, add a clear indicator while prone to getting the *Spore* status effect, or make it easier to see while inside spore clouds/areas.

## How to use

Upon installing the mod the Balanced preset is picked by default which is recommended for most players. If that suffices you don't have to do anything else. Although if you want something else, you can pick another preset or create your own.

Open the config file or use [PEAKLib.ModConfig](https://thunderstore.io/c/peak/p/PEAKModding/ModConfig/) if installed, then in the `Host` tab you will see the `preset` option:

1. **Subtle**: both new mechanics on, everything else barely nudged, still pretty similar to vanilla difficulty.
2. **Balanced (default)**: a bit more forgiving than Subtle, the tuning I'd ship if this were the base game's own balance pass.
3. **Generous**: meaningfully easier, most of the RNG-driven unfairness smoothed out.
4. **Tame**: if you don't want to bother with this biome's shenanigans at all.
5. **Custom**: nothing but vanilla until you say otherwise. Every setting ships at its unmodded value, so picking Custom and changing nothing plays exactly like the base game, here you can freely configure everything about this mod. This is also the preset to use if you want to make Roots harder rather than easier.

- *Example 1:* let's say you dislike spore areas and only want to remove those, you can with the Custom preset, then just enable `Spore-Areas/disable-spore-areas` and you're golden!
- *Example 2:* maybe you like the Subtle preset, but want to additionally make climbing fully counter wind as well, select the Subtle preset, disable `Host/apply-pure-preset` and enable `Wind/climb-shelters-from-wind`.

> **Note:** presets 1-4 ignore most game-changing settings entirely and apply their own values, if you'd like to configure everything yourself pick the Custom preset.

Every value each preset uses is listed, setting by setting, in [`docs/PRESETS.md`](https://github.com/OnlyCook/peak-fairoots/blob/main/docs/PRESETS.md).

## Why

Roots is only the second biome and is (subjectively) the least balanced: on some seeds you can beat it without ever touching anything but tree leaves and on others you question the energy used to play this game. Although often not because of it being overly challenging but because of how unfair and frustrating it feels. I think this is caused by the fact that there are too little mechanics to address the many new hazards. This mod tries to fix exactly that while also making it more tolerable to traverse this biome in general.

## Notes

- This mod **won't work correctly** if not all clients have it installed.
- The host decides all game-changing values. Clients can still adjust any setting in the `General` tab which will only affect their own game though.
- Translations were done by AI, so if something is off in your language you are free to contact me (see below).

## Feedback & bug reports

Found a bug or have a suggestion? Please **[fill out this form](forms.gle/PLACEHOLDER)** or send me an email at `theactualcooker@gmail.com`.

## Requirements

- [BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/) `5.4.2403`

## For players

- You can install the mod through r2modman as `Fairoots`,
- On [Thunderstore](https://thunderstore.io/c/peak/p/OnlyCook/Fairoots/),
- Or on [Nexus Mods](https://www.nexusmods.com/peak/mods/198)

## For developers

- [`ROADMAP.md`](ROADMAP.md): full feature spec, phased plan, status, handoff notes.
- [`CODEBASE.md`](CODEBASE.md): where code lives in `src/` and `tests/`.
- [`docs/PRESETS.md`](docs/PRESETS.md): source of truth for every balance number.
- [`docs/TESTING.md`](docs/TESTING.md): manual in-game test loop + automated test suite.

Build:
```bash
cd src/Fairoots
dotnet build -c Release                         # -> bin/Release/Fairoots.dll
dotnet build -c Release -p:DeployToProfile=true # also copy into the r2modman profile
```

Tests (no game install required):
```bash
cd tests/Fairoots.Tests
dotnet test
```
