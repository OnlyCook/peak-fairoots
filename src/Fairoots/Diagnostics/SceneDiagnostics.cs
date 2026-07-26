using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zorro.Core; // Singleton<T> (Zorro.Core.Runtime.dll) - MapHandler's base

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// The Phase 3 runtime-logging harness (ROADMAP.md): a one-shot scan of a
    /// loaded level that reports what Fairoots can and can't find - the current
    /// biome, the wind zone and its live field values, spore-bomb candidates (by
    /// the <c>SporeFungus</c>/<c>SporeMushroom</c>/<c>SporeMushroomExplo</c> name
    /// substrings from RESEARCH.md Q7) with their attached hazard components and
    /// Inspector-configured values, spore-area emitters, and creatures.
    ///
    /// It resolves the open questions static decompilation can't (exact Roots
    /// prefab names, real trigger/knockback/shake values, which StatusEmitter
    /// subclass spore areas use, whether wind's obstacle raycast is on) by reading
    /// them straight off the live objects. Purely diagnostic - it reads and logs,
    /// never mutates the scene. Every section is independently guarded so one bad
    /// lookup can't abort the rest of the report or destabilise the game.
    /// </summary>
    internal static class SceneDiagnostics
    {
        private static float _lastDump = -999f;

        /// <summary>Debounced auto-trigger, used by the level-generated postfix (which can fire several times per load).</summary>
        internal static void AutoDump(string reason)
        {
            if (Time.time - _lastDump < 2f)
            {
                return;
            }

            _lastDump = Time.time;
            DumpReport(reason);
        }

        /// <summary>
        /// Phase 4 research tool (ROADMAP.md/RESEARCH.md Q7 open item): probes the
        /// spore-bomb candidate nearest the local player for whatever geometry might
        /// identify "sitting inside bush/grass" - procedural grass-blade density
        /// (<see cref="GrassChunk"/>, the only foliage system visible to static
        /// decompilation) plus a broad nearby-collider/renderer name dump to catch
        /// any decorative bush/foliage prefab that isn't. Not part of the shipped
        /// cull feature - purely diagnostic, reads only, never mutates the scene.
        /// Intended use: stand next to a spore bomb that's visibly camouflaged in
        /// foliage in-game, press the probe hotkey, compare the numbers against a
        /// spore bomb standing in the open.
        /// </summary>
        internal static void ProbeFoliageNearestSporeBomb()
        {
            Diag.Info("===== Fairoots foliage probe =====");
            try
            {
                var player = Character.localCharacter;
                if (player == null)
                {
                    Diag.Info("[FoliageProbe] MISSING: no local Character yet");
                    return;
                }

                Vector3 playerPos = player.Center;

                Transform nearest = null;
                float nearestDist = float.MaxValue;
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (ClassifySporeBomb(t.name) == null)
                    {
                        continue;
                    }

                    float d = Vector3.Distance(t.position, playerPos);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = t;
                    }
                }

                if (nearest == null)
                {
                    Diag.Info("[FoliageProbe] MISSING: no spore-bomb candidates found in scene");
                    return;
                }

                Vector3 pos = nearest.position;
                Diag.Info($"[FoliageProbe] nearest candidate \"{nearest.name}\" @ {Fmt(pos)}, {nearestDist:0.0}m from player");

                // Grass-blade density: only known code-level foliage system
                // (BasicGrassSpawner/GrassChunk - real per-blade world positions,
                // not a mesh/collider). Count blades within a few radii.
                var chunks = UnityEngine.Object.FindObjectsOfType<GrassChunk>(true);
                int total = 0;
                foreach (var c in chunks)
                {
                    if (c.GrassPoints != null)
                    {
                        total += c.GrassPoints.Count;
                    }
                }

                Diag.Info($"[FoliageProbe] {chunks.Length} GrassChunk(s) in scene, {total} blade(s) total");
                foreach (float r in new[] { 1f, 2f, 3f, 5f })
                {
                    int count = 0;
                    foreach (var c in chunks)
                    {
                        if (c.GrassPoints == null)
                        {
                            continue;
                        }

                        foreach (var p in c.GrassPoints)
                        {
                            if (Vector3.Distance(((Vector3)p.worldPos), pos) <= r)
                            {
                                count++;
                            }
                        }
                    }

                    Diag.Info($"[FoliageProbe]   blades within {r:0}m: {count}");
                }

                // Broad nearby-collider dump - catches anything with a hitbox
                // (bush/rock/foliage prefab), grass blades have none.
                var cols = Physics.OverlapSphere(pos, 3f);
                Diag.Info($"[FoliageProbe] {cols.Length} collider(s) within 3m:");
                foreach (var c in cols)
                {
                    Diag.Info($"[FoliageProbe]   collider \"{c.gameObject.name}\" tag={c.tag} layer={LayerMask.LayerToName(c.gameObject.layer)}");
                }

                // Broad nearby-renderer dump - catches decorative meshes with no
                // collider at all (most likely home for a purely-visual bush prefab).
                // Skip anything attached to a Character (status-effect VFX like
                // Glow/VFX_Hot follow the player, not the plant, and swamp the list).
                int shown = 0, skippedCharacter = 0;
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                {
                    if (Vector3.Distance(r.transform.position, pos) > 3f)
                    {
                        continue;
                    }

                    if (r.GetComponentInParent<Character>() != null)
                    {
                        skippedCharacter++;
                        continue;
                    }

                    if (shown++ >= 40)
                    {
                        Diag.Info("[FoliageProbe]   ...(more renderers within 3m, truncated)");
                        break;
                    }

                    // Print the immediate parent chain (not walked all the way to
                    // the scene root, which turned out to be a big shared "Biome_2"
                    // container for everything) so the actual prefab instance name
                    // this mesh belongs to is visible.
                    var chain = new List<string>();
                    var anc = r.transform.parent;
                    for (int i = 0; i < 5 && anc != null; i++, anc = anc.parent)
                    {
                        chain.Add(anc.name);
                    }

                    Diag.Info(
                        $"[FoliageProbe]   renderer \"{r.gameObject.name}\" @ {Fmt(r.transform.position)} " +
                        $"tag={r.tag} layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                        $"boundsSize={Fmt(r.bounds.size)} parents=[{string.Join(" < ", chain)}]");
                }

                Diag.Info($"[FoliageProbe] (skipped {skippedCharacter} character-attached renderer(s) within 3m)");
            }
            catch (System.Exception e)
            {
                Diag.Error($"[FoliageProbe] threw: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                Diag.Info("===== end foliage probe =====");
            }
        }

        internal static void DumpReport(string reason)
        {
            _lastDump = Time.time;
            Diag.Info($"===== Fairoots scene diagnostics ({reason}) =====");
            ReportBiome();
            ReportWind();
            ReportSporeBombs();
            ReportSporeAreas();
            ReportCreatures();
            ReportFoliageGroups();
            Diag.Info("===== end diagnostics =====");
        }

        /// <summary>
        /// Phase 4 research (follow-up to <see cref="ProbeFoliageNearestSporeBomb"/>):
        /// the F10 probe confirmed one camouflaging clump type ("Fern", grouped under
        /// a "Ferns (N)" container inside "PlateauProps"), which has no collider - the
        /// scene has no dedicated foliage class to query, only scene-authored naming.
        /// This catalogs every sibling group under each "PlateauProps" container so we
        /// know the *complete* set of clump type names to allowlist (Ferns plus
        /// whatever else exists - bushes, shrubs, etc.), not just the one instance the
        /// F10 probe happened to be standing next to.
        /// </summary>
        private static void ReportFoliageGroups()
        {
            Section("FoliageGroups", () =>
            {
                // Only containers actually under "Roots Segment" - the scene loads
                // every biome's PlateauProps at once, and non-Roots set-dressing
                // (BeachGrass, Urchins, Palms, ...) would otherwise pollute the
                // allowlist since this mod only ever acts in Roots.
                var propsContainers = UnityEngine.Object.FindObjectsOfType<Transform>(true)
                    .Where(t => t.name == "PlateauProps" && IsUnderRootsSegment(t))
                    .ToList();

                Diag.Info($"[FoliageGroups] found {propsContainers.Count} \"PlateauProps\" container(s) under Roots Segment");

                var groupCounts = new Dictionary<string, int>();
                var groupChildCounts = new Dictionary<string, int>();
                foreach (var props in propsContainers)
                {
                    for (int i = 0; i < props.childCount; i++)
                    {
                        var child = props.GetChild(i);
                        groupCounts[child.name] = groupCounts.TryGetValue(child.name, out var n) ? n + 1 : 1;
                        groupChildCounts[child.name] = groupChildCounts.TryGetValue(child.name, out var cn)
                            ? cn + child.childCount
                            : child.childCount;
                    }
                }

                foreach (var kv in groupCounts.OrderByDescending(kv => kv.Value))
                {
                    Diag.Info($"[FoliageGroups]   group \"{kv.Key}\" x{kv.Value} instance(s), {groupChildCounts[kv.Key]} total children");
                }
            });
        }

        private static bool IsUnderRootsSegment(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name == "Roots Segment")
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReportBiome()
        {
            Section("Biome", () =>
            {
                var mh = Singleton<MapHandler>.Instance;
                if (mh == null)
                {
                    Diag.Info("[Biome] MISSING: MapHandler singleton not available yet");
                    return;
                }

                var biome = mh.GetCurrentBiome();
                Diag.Info($"[Biome] OK current={biome}, RootsPresent={mh.BiomeIsPresent(Biome.BiomeType.Roots)}, segment={mh.GetCurrentSegment()}");
                if (biome != Biome.BiomeType.Roots)
                {
                    Diag.Info("[Biome] NOTE: not currently in Roots - Roots hazard tuning would be inactive here.");
                }
            });
        }

        private static void ReportWind()
        {
            Section("Wind", () =>
            {
                var rw = RootsWind.instance;
                if (rw == null || rw.windZone == null)
                {
                    Diag.Info("[Wind] MISSING: RootsWind.instance / windZone not present (expected outside Roots)");
                    return;
                }

                var wz = rw.windZone;
                Diag.Info(
                    $"[Wind] OK WindChillZone: windForce={wz.windForce}, windMovesItems={wz.windMovesItems}, " +
                    $"windItemFactor={wz.windItemFactor}, useRaycast={wz.useRaycast} " +
                    $"(min/maxRaycastDistance={wz.minRaycastDistance}/{wz.maxRaycastDistance}), windActive={wz.windActive}");
                Diag.Info(
                    "[Wind] -> useRaycast is the obstacle-occlusion lever (RESEARCH.md Q5/open-Q5); " +
                    "note whether it's True on the real Roots instance.");
            });
        }

        private static void ReportSporeBombs()
        {
            Section("SporeBombs", () =>
            {
                var matches = new List<(Transform t, string kind)>();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    string kind = ClassifySporeBomb(t.name);
                    if (kind != null)
                    {
                        matches.Add((t, kind));
                    }
                }

                Diag.Info($"[SporeBombs] found {matches.Count} candidate(s) by name substring");
                if (matches.Count == 0)
                {
                    Diag.Info("[SporeBombs] MISSING: none found - confirm the Roots-biome prefab names, or scan while standing in Roots.");
                    return;
                }

                // Distinct exact names + counts - this is what confirms the real Roots prefix.
                foreach (var g in matches.GroupBy(m => m.t.name).OrderByDescending(g => g.Count()))
                {
                    Diag.Info($"[SporeBombs]   name \"{g.Key}\" x{g.Count()}");
                }

                // Structure probe: the name-matched object often does NOT carry the
                // hazard components itself - they live on a parent, the scene root,
                // or are spawned on trigger. Dump the real component layout for a few
                // samples of each distinct name so we can find where AOE/explosion/
                // status logic actually is (needed for the knockback/radius/shake
                // sub-features; culling only needs the name+position, which we have).
                foreach (var g in matches.GroupBy(m => m.t.name))
                {
                    int sampled = 0;
                    foreach (var (t, kind) in g)
                    {
                        if (sampled++ >= 2)
                        {
                            break;
                        }

                        ProbeSporeBombStructure(t, kind);
                    }
                }

                // Also count the hazard components across the whole scene - tells us
                // whether they exist pre-trigger at all (0 => spawned on explosion).
                Diag.Info(
                    $"[SporeBombs] scene-wide component counts: " +
                    $"AOE={UnityEngine.Object.FindObjectsOfType<AOE>(true).Length}, " +
                    $"ExplosionEffect={UnityEngine.Object.FindObjectsOfType<ExplosionEffect>(true).Length}, " +
                    $"AddScreenshake={UnityEngine.Object.FindObjectsOfType<AddScreenshake>(true).Length}");
            });
        }

        /// <summary>
        /// Dump what a spore bomb's <c>TriggerEvent.triggerEvent</c> actually
        /// invokes on detonation. The list is a serialized <c>UnityEvent</c>, so it's
        /// invisible in a decompile - but <c>SpawnGameObject.Go</c> (the seam the
        /// explosion tuning patches) may well not be the only listener, and anything
        /// else in there is a detonation side effect Fairoots currently doesn't see.
        /// </summary>
        private static void LogTriggerEventListeners(GameObject go)
        {
            var trigger = go.GetComponent<TriggerEvent>();
            if (trigger == null || trigger.triggerEvent == null)
            {
                return;
            }

            int count = trigger.triggerEvent.GetPersistentEventCount();
            if (count == 0)
            {
                Diag.Info("[SporeBombs]     TriggerEvent listeners: none persistent (runtime-added only)");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var target = trigger.triggerEvent.GetPersistentTarget(i);
                Diag.Info(
                    $"[SporeBombs]     TriggerEvent listener[{i}]: " +
                    $"{(target != null ? target.GetType().Name + " on \"" + target.name + "\"" : "<null target>")}" +
                    $".{trigger.triggerEvent.GetPersistentMethodName(i)}()");
            }
        }

        private static void ProbeSporeBombStructure(Transform t, string kind)
        {
            var go = t.gameObject;
            Diag.Info($"[SporeBombs]   probe {kind} @ {Fmt(t.position)} name=\"{t.name}\"");
            Diag.Info($"[SporeBombs]     self components: {ComponentNames(go)}");
            LogTriggerEventListeners(go);

            // Walk up to the root, logging each ancestor's components.
            int depth = 0;
            for (var p = t.parent; p != null && depth < 4; p = p.parent, depth++)
            {
                Diag.Info($"[SporeBombs]     parent[{depth}] \"{p.name}\": {ComponentNames(p.gameObject)}");
            }

            // Where do the hazard components resolve from here?
            var aoe = go.GetComponentInParent<AOE>();
            var aoeInRoot = t.root.GetComponentInChildren<AOE>(true);
            var emitterInRoot = t.root.GetComponentInChildren<StatusEmitter>(true);
            Diag.Info(
                $"[SporeBombs]     resolve: AOE-in-parent={(aoe ? aoe.gameObject.name : "none")}, " +
                $"AOE-in-root={(aoeInRoot ? aoeInRoot.gameObject.name : "none")}, " +
                $"StatusEmitter-in-root={(emitterInRoot ? emitterInRoot.gameObject.name : "none")}");
            if (aoe != null)
            {
                Diag.Info(
                    $"[SporeBombs]     AOE values: range={aoe.range}, knockback={aoe.knockback}, " +
                    $"minFactor={aoe.minFactor}, factorPow={aoe.factorPow}, " +
                    $"statusType={aoe.statusType}, statusAmount={aoe.statusAmount}");
            }
        }

        private static string ComponentNames(GameObject go)
        {
            var comps = go.GetComponents<Component>();
            return comps.Length == 0
                ? "(none)"
                : string.Join(", ", comps.Select(c => c == null ? "<missing-script>" : c.GetType().Name));
        }

        private static void ReportSporeAreas()
        {
            Section("SporeAreas", () =>
            {
                var emitters = UnityEngine.Object.FindObjectsOfType<StatusEmitter>(true);
                Diag.Info($"[SporeAreas] found {emitters.Length} StatusEmitter(s) total (not all are spores - grouped below)");
                if (emitters.Length == 0)
                {
                    return;
                }

                int windAffected = emitters.Count(e => e is WindAffectedStatusEmitter);
                Diag.Info($"[SporeAreas] {windAffected} of {emitters.Length} are WindAffectedStatusEmitter (wind already suppresses those)");

                // Group by (statusType, wind-affected) so the Spores emitters surface
                // instead of being buried under Hot/heat-source emitters. Show one
                // representative's values per group.
                foreach (var g in emitters
                             .GroupBy(e => (type: e.statusType, wind: e is WindAffectedStatusEmitter))
                             .OrderByDescending(g => g.Count()))
                {
                    var sample = g.First();
                    Diag.Info(
                        $"[SporeAreas]   {g.Key.type}{(g.Key.wind ? " (WindAffected)" : "")} x{g.Count()} " +
                        $"e.g. amount={sample.amount}, radius={sample.radius}, outerFade={sample.outerFade}, " +
                        $"innerFade={sample.innerFade}, minAmount={sample.minAmount}");
                }

                // Spotlight the actual spore emitters with positions.
                var spores = emitters.Where(e => e.statusType == CharacterAfflictions.STATUSTYPE.Spores).ToList();
                Diag.Info($"[SporeAreas] Spores-type emitters: {spores.Count} " +
                          $"({spores.Count(e => e is WindAffectedStatusEmitter)} WindAffected)");
                int shown = 0;
                foreach (var e in spores)
                {
                    if (shown++ >= 10)
                    {
                        break;
                    }

                    Diag.Info(
                        $"[SporeAreas]   {(e is WindAffectedStatusEmitter ? "WindAffected " : "")}Spores " +
                        $"amount={e.amount}, radius={e.radius} @ {Fmt(e.transform.position)}");
                }
            });
        }

        private static void ReportCreatures()
        {
            Section("Creatures", () =>
            {
                var zm = UnityEngine.Object.FindObjectOfType<ZombieManager>(true);
                Diag.Info(zm == null
                    ? "[Creatures] ZombieManager: MISSING"
                    : $"[Creatures] ZombieManager OK: maxActiveZombies={zm.maxActiveZombies}");

                int zombies = UnityEngine.Object.FindObjectsOfType<MushroomZombie>(true).Length;
                int beetles = UnityEngine.Object.FindObjectsOfType<Beetle>(true).Length;
                int spiders = UnityEngine.Object.FindObjectsOfType<Spider>(true).Length;
                Diag.Info($"[Creatures] live counts: MushroomZombie={zombies}, Beetle={beetles}, Spider={spiders}");
            });
        }

        /// <summary>Match the three known hazard variants, longest substring first (RESEARCH.md Q7).</summary>
        private static string ClassifySporeBomb(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (name.IndexOf("SporeMushroomExplo", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Explosive Spore Bomb (SporeMushroomExplo)";
            }

            if (name.IndexOf("SporeMushroom", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Poison Spore Bomb (SporeMushroom)";
            }

            if (name.IndexOf("SporeFungus", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Spore Bomb (SporeFungus)";
            }

            return null;
        }

        private static string Fmt(Vector3 p) =>
            $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) -> grid {Fairoots.Core.GridPos.Round(p.x, p.y, p.z)}";

        private static void Section(string name, System.Action body)
        {
            try
            {
                body();
            }
            catch (System.Exception e)
            {
                Diag.Error($"[{name}] diagnostics section threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
