# Changelog

## Unreleased

- Wind can no longer ragdoll you off an edge (`Wind/prevent-wind-ragdoll`, on by
  default under every preset): when a gust pushes you off, you stay in control on
  the way down instead of flailing, so you can still grab a wall or fire a Rescue
  Hook. Only applies to falls wind actually caused — walking off a ledge yourself
  ragdolls exactly like vanilla. Turn it off for vanilla behaviour.
- New `Spore-Bombs/disable-foliage-removal` (off by default) for anyone who wants
  spore bombs hidden inside bushes and ferns left where the game put them. It
  doesn't mean more spore bombs overall: the removal target still applies, the
  removal just stops treating camouflaged bombs as the first to go.
- Both on-screen warnings ("Breathing in spores!", "Spider dropping on you!") are
  now translated into all 14 languages the game ships with, and follow the game's
  language setting live.
- Fixed: creature settings the host changed mid-run did nothing for everyone
  else — beetles and spiders stayed visible on other players' screens after the
  host disabled them, and the speed/knockback/ragdoll dials likewise only applied
  to whoever changed them.
- Every setting outside `General` and `Debug` is now host-only, with no
  exceptions: the two wind-preceded-fall settings
  (`fall-camera-dampen-clamp`, `fall-camera-dampen-window-seconds`) used to be
  per-player and are now the host's call like everything else.

- New `Spores` section with two settings that apply to the Spores status itself
  rather than to one hazard: `clear-time-multiplier` (0.7 default) changes how
  long spores take to wear off once you're out of them — 0.5 means half the time,
  including the short pause before the meter starts dropping — and
  `build-up-multiplier` scales every dose of spores you're given, whatever gave it
  to you: a spore area, a spore bomb's cloud, or a zombie's bite. The build-up
  setting stacks on top of the per-hazard settings, so it's left at vanilla (1.0)
  by default and no preset changes it — it does nothing until you set it yourself.

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
  hazard.
- A spore bomb's visible cloud now grows and shrinks with
  `Spore-Bombs/spore-area-radius-multiplier`, so it matches the area that
  actually applies spores.

## 0.1.0

- Repo scaffold only: BepInEx plugin skeleton, packaging pipeline, no gameplay
  changes yet. Not published to Thunderstore/Nexus.
