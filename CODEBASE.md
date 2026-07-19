# CODEBASE — where things live in `src/`

No gameplay code exists yet (repo-setup phase — see `ROADMAP.md`). This file
will grow into a brief map of `src/Fairoots/` (what each file/folder is
responsible for) the same way `peak-sense-of-direction/CODEBASE.md` and
`peak-checkpoint-save`'s structure do, so a reader can find where a given
mechanic lives without re-scanning the whole tree.

## Current layout

- `src/Fairoots/` — the BepInEx plugin project.
  - `Plugin.cs` — entry point (`BepInPlugin`), currently just loads and logs.
  - `PluginConfig.cs` — config binding, currently empty. Will hold the seed
    field, the preset 1-4 selector, and per-mechanic override settings once
    the first mechanic lands.
  - `PluginInfo.cs` — GUID/name/version constants.
- `tests/Fairoots.Tests/` — xunit project for the seed/preset decision logic
  (pure C#, no game/BepInEx dependency so it runs anywhere). See ROADMAP.md
  "Testing strategy."
- `packaging/` — Thunderstore/Nexus packaging pipeline (`build-release.sh`,
  `gen-readme.sh`, `manifest.json`, `CHANGELOG.md`), same pattern as the
  other two PEAK mods in this GitHub account.
- `docs/TESTING.md` — manual in-game test loop (build/deploy/log locations).

## Planned structure (fills in as phases land — see ROADMAP.md)

Expect one folder per mechanic group, mirroring `OVERVIEW.md`'s sections:
`Wind/`, `SporeBombs/`, `SporeAreas/`, `Creatures/`, plus a shared
`Seed/` (or `Rng/`) folder holding the deterministic sub-RNG helper that
every mechanic's spawn/probability decisions route through, and `Presets/`
holding the 4 preset definitions.
