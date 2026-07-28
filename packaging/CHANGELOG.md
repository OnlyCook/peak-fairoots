# Changelog

## Unreleased

- Spore clouds can now be made see-through (`General/spore-area-cloud-opacity`
  and `General/spore-bomb-cloud-opacity`, 0.35 by default) so the game's own
  Spores screen overlay is readable through them — in vanilla the cloud and the
  overlay are the same colour, so it's hard to tell whether you're actually
  inside one. Cosmetic and per-player; the hazard's size and rate are unchanged.
- The spores screen overlay now stays up for as long as you're standing in a
  spore bomb's cloud (`General/show-overlay-in-spore-bomb-clouds`, on by
  default), the way it already does in a mushroom spore cloud — in vanilla a
  bomb's cloud only flashes when it damages you, with nothing in between saying
  you're still in it. The damage flash is untouched.
- Optional on-screen "Breathing in spores!" warning
  (`General/show-spore-cloud-label`, off by default) in the game's own font and
  the Spores status colour, shown whenever you're standing in spores — either
  hazard. English only for now.
- A spore bomb's visible cloud now grows and shrinks with
  `Spore-Bombs/spore-area-radius-multiplier`, so it matches the area that
  actually applies spores.

## 0.1.0

- Repo scaffold only: BepInEx plugin skeleton, packaging pipeline, no gameplay
  changes yet. Not published to Thunderstore/Nexus.
