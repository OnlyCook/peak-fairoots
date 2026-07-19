# Testing guide

Two layers, per ROADMAP.md's testing strategy:

1. **Automated unit tests** (`tests/Fairoots.Tests/`, `dotnet test`) — cover
   the seed-deterministic decision logic in isolation (no game install
   needed). This is the primary net for "did the RNG change actually apply
   correctly," since playing it out by hand can't easily prove a spawn count
   or a seed's reproducibility. See ROADMAP.md "Testing strategy" for the full
   rationale.

   Run them with:

   ```bash
   cd tests/Fairoots.Tests
   dotnet test
   ```

   Current coverage (Phase 2 — seed/preset core, no game install required):
   - **Determinism** (`DeterministicHashTests`): the `(seed, mechanic,
     position) → value` hash is stable for identical inputs, uniform enough to
     use as a probability, and decorrelated across different seeds and
     mechanic tags. Guards the "same seed = same result" premise.
   - **Spore-bomb cull budget** (`SporeBombCullTests`): same seed culls the
     same *specific* objects (not just the same count); the two-pass budget
     matches ROADMAP.md's worked example (foliage removal counts toward, not
     on top of, the seeded target, and never overshoots it); the selection is
     independent of input order (so it's multiplayer-consistent).
   - **Preset resolution** (`PresetResolutionTests`): a hand-set config value
     always wins over the active preset and is never clobbered by switching
     presets; preset cull fractions match the ROADMAP table and increase with
     preset strength.
2. **Manual in-game loop** (this doc) — for anything only observable at
   runtime (feel, visual clutter, screen shake, actual spawn positions in a
   real level).

## Build & deploy in one command

```bash
cd src/Fairoots
dotnet build -c Release -p:DeployToProfile=true
```

Copies `Fairoots.dll` into
`~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/plugins/OnlyCook-Fairoots/`.

Then in **r2modman**: make sure *BepInExPack PEAK* is installed & enabled in
the same profile, and click **Start modded**.

## Where the logs are

`~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/LogOutput.log`
(rewritten every launch). Filter for our lines:

```bash
grep -iE "Fairoots" ~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/LogOutput.log
```

## Test checklist

Nothing to test yet — repo-setup phase only, no gameplay code. This section
grows alongside ROADMAP.md's phases (one checklist entry per shipped
mechanic, mirroring `peak-checkpoint-save/docs/TESTING.md`'s format: pre-req,
numbered steps, what to report back).
