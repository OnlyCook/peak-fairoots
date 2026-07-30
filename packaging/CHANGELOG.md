# Changelog

## Unreleased

- **Every setting now starts at its vanilla value.** Install the mod, set
  `General/preset` to `Custom`, change nothing, and Roots plays exactly like the
  unmodded game — every multiplier 1.0, every removal fraction 0, every new
  mechanic off. Custom is a blank slate you build up one dial at a time instead of
  Balanced's numbers with the labels showing. Presets 1-4 are unaffected: they
  ignore your per-mechanic settings and apply their own values, as before.
- Settings that used to apply the same under every preset are now preset-driven,
  which follows from the above: thrown-item knockouts, the blowgun-vs-creatures
  rule, backpack wind immunity, wind-ragdoll immunity, the climb shelter, foliage
  removal, cover-your-mouth and the creature wind dials all get a per-preset value
  now, and get gentler or stronger with the preset you pick rather than sitting at
  one number. Balanced keeps the numbers it already shipped with, so the default
  preset feels the same as before. Only the `disable-*` kill switches, the keybinds
  and `Debug` stay flat.
- Renamed for that reason: `Spore-Bombs/disable-foliage-removal` is now
  `Spore-Bombs/enable-foliage-removal`, and `Spore-Areas/disable-cover-mouth` is
  now `Spore-Areas/enable-cover-mouth` — both default off (vanilla) and are turned
  on by every preset. If you had either set by hand, set it again under the new
  key.
- Every balance setting, its default and its value under each of the four presets
  is now documented in one table in the repo (`docs/PRESETS.md`), which is also
  what the mod is built from — so the listed numbers can't drift out of step with
  what actually ships.

- Wind can no longer ragdoll you off an edge (`Wind/prevent-wind-ragdoll`, on
  under every preset): when a gust pushes you off, you stay in control on
  the way down instead of flailing, so you can still grab a wall or fire a Rescue
  Hook. Only applies to falls wind actually caused — walking off a ledge yourself
  ragdolls exactly like vanilla. Turn it off for vanilla behaviour.
- Spore bombs hidden inside bushes and ferns can be left where the game put them
  (`Spore-Bombs/enable-foliage-removal`, on under every preset — switch it off
  under Custom). Switching it off doesn't mean more spore bombs overall: the
  removal target still applies, the removal just stops treating camouflaged bombs
  as the first to go.
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
