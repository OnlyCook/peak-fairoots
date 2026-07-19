<!-- GENERATED FILE — do not edit by hand.
     Source: packaging/README.md + packaging/README.github-extra.md
     Regenerate with: bash packaging/gen-readme.sh -->

# Fairoots

Makes the Roots biome more fair and balanced.

**Status: early development, not yet released.** This listing is a placeholder
until the first real preset lands — see the GitHub repo's `ROADMAP.md` for the
full plan.

## What it does

Roots is PEAK's second biome, and it swings hard between "beaten without
touching anything but tree leaves" and "why does this game hate me" runs —
usually not because it's genuinely harder, but because a handful of its
hazards (spore bombs, spore clouds, wind, zombies/beetles) lean heavily on
raw RNG with too few mechanics to counter them. Fairoots rebalances those
hazards — cutting some spore bomb spawns, softening wind and knockback,
calming down aggressive creatures, adding a couple of new counterplay
mechanics — through **four selectable presets**, from a light touch-up to a
fully tamed climb-focused biome.

Every randomized decision Fairoots makes (which spore bombs get cut, etc.) is
bound to your run's seed: same seed + same Roots level = same result, every
time. Not more randomness on top of randomness.

## Presets

1. **Subtle** — the two new counterplay mechanics, light balancing, nothing
   drastic.
2. **Balanced (default)** — a bit more forgiving than Subtle; the tuning the
   maintainer would ship if this were the base game's own balance pass.
3. **Generous** — meaningfully easier, most RNG-driven unfairness smoothed
   out.
4. **Tame** — Roots stops fighting you over RNG; climb it.

## Requirements

- [BepInEx PEAK pack](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)

## Installation (manual, without a mod manager)

1. Install [BepInEx PEAK pack](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/).
2. Extract this mod's zip into `BepInEx/plugins/OnlyCook-Fairoots/`.

## Development

```bash
cd src/Fairoots
dotnet build -c Release -p:DeployToProfile=true
```

See `docs/TESTING.md` for the manual in-game test loop, and `tests/` for the
automated unit tests covering the seed-deterministic RNG logic (`dotnet test`
from `tests/Fairoots.Tests/`, no game install required).

## License

MIT — see `LICENSE`.
