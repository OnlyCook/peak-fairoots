## Requirements

- [BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/) `5.4.2403`

## For players

Not yet published on r2modman, Thunderstore or Nexus Mods (see the "Status"
note above). Until then, install manually:

1. Install [BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/).
2. Extract this mod's zip into `BepInEx/plugins/OnlyCook-Fairoots/`.

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
