# CODEBASE — where things live in `src/`

A brief map of `src/Fairoots/` (what each file/folder is responsible for), the
same way `peak-sense-of-direction/CODEBASE.md` and `peak-checkpoint-save`'s
structure do, so a reader can find where a given mechanic lives without
re-scanning the whole tree.

**Phase 2 (seed/preset core) is in.** The Harmony patches that consume it
(Phase 4+ — spore bombs, wind, etc.) are not written yet; see `ROADMAP.md`'s
phased plan.

## The Core / game-facing split (read this first)

The single most important structural rule: **all of the mod's actual decision
logic lives in `src/Fairoots/Core/`, which is 100% Unity/BepInEx-free.** It
depends only on `System.*`. That is what lets the test project compile the same
source (via a `<Compile Include>` glob — no game install needed) and prove the
in-game behavior directly. Anything that touches `UnityEngine.*`, `BepInEx.*`,
or Harmony lives *outside* `Core/` (in the project root or future mechanic
folders) and calls into `Core/` for every probabilistic/seeded decision.

If you're adding logic and it doesn't strictly need a Unity type, it belongs in
`Core/` with a unit test — not in a Harmony patch.

## Current layout

- `src/Fairoots/` — the BepInEx plugin project (game-facing).
  - `Plugin.cs` — entry point (`BepInPlugin`); loads config + Harmony, logs.
  - `PluginConfig.cs` — config binding: the `seed` field, the `preset` 1-4
    selector, and per-mechanic override entries (default to a "follow preset"
    sentinel). Exposes *resolved* accessors (e.g. `SporeBombCullFraction`) that
    fold preset + override together via `Core/Presets/OverrideResolution`.
  - `PluginInfo.cs` — GUID/name/version constants.
  - **`Core/`** — the pure, Unity-free decision layer (see split rule above):
    - `GridPos.cs` — a world position rounded to an integer grid; the stable
      per-object identity every seeded decision keys off. `GridPos.Round(...)`
      is the one rounding definition the whole mod uses.
    - `DeterministicHash.cs` — the determinism engine: a hand-rolled FNV-1a +
      murmur-finalizer hash mapping `(seed, mechanicTag, GridPos)` → a stable
      value / rank key, identical on every runtime and every launch.
      Deliberately **not** `HashCode.Combine`/`string.GetHashCode`/`System.Random`
      (all per-process-randomized or runtime-dependent — would break the seed
      guarantee). See its file comment.
    - `SporeBombCull.cs` — the two-pass spore-bomb removal decision (pure
      arithmetic + seeded ranked selection): unconditional foliage removal,
      then a seeded cull budgeted against it. Returns per-candidate outcomes;
      the (future) Harmony patch maps them back onto real GameObjects.
    - `Presets/PresetId.cs` — the 1-4 preset enum (Balanced is default).
    - `Presets/PresetCatalog.cs` — the per-preset numeric values (single source
      of truth). Currently: spore-bomb cull fraction + always-on mechanic
      flags; grows one entry per mechanic as its phase lands.
    - `Presets/OverrideResolution.cs` — non-destructive preset resolution
      (sentinel pattern): a player-set value always wins over the preset.
- `tests/Fairoots.Tests/` — xUnit project. Links `src/Fairoots/Core/**/*.cs`
  directly (no game/BepInEx dependency, runs anywhere). One test file per Core
  area; see `docs/TESTING.md` for what each covers.
- `packaging/` — Thunderstore/Nexus packaging pipeline (`build-release.sh`,
  `gen-readme.sh`, `manifest.json`, `CHANGELOG.md`), same pattern as the
  other two PEAK mods in this GitHub account.
- `docs/TESTING.md` — automated-test coverage summary + manual in-game loop.

## Planned structure (fills in as phases land — see ROADMAP.md)

Expect one game-facing folder per mechanic group, mirroring `OVERVIEW.md`'s
sections: `Wind/`, `SporeBombs/`, `SporeAreas/`, `Creatures/` — each holding
the Harmony patches that scan the scene and apply removals/tweaks, delegating
every seeded decision to `Core/`. New per-mechanic preset values land in
`Core/Presets/PresetCatalog.cs` as each phase is implemented.
