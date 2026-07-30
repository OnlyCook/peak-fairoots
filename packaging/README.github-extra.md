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
