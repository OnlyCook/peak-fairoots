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
