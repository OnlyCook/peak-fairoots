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
5. **Custom** — nothing but vanilla until you say otherwise. Every individual
   setting ships at its unmodded value, so picking Custom and changing nothing
   plays exactly like the base game; turn on one dial at a time and you can
   actually tell what each one does. The four presets above ignore these settings
   and apply their own numbers.

Every value each preset uses is listed, setting by setting, in
[`docs/PRESETS.md`](https://github.com/OnlyCook/peak-fairoots/blob/main/docs/PRESETS.md) —
and that table is what the mod is built from, so it can't drift out of step with
what actually ships.

## Requirements

- [BepInEx PEAK pack](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)
