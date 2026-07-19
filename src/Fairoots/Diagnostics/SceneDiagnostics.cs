using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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

        internal static void DumpReport(string reason)
        {
            _lastDump = Time.time;
            Diag.Info($"===== Fairoots scene diagnostics ({reason}) =====");
            ReportBiome();
            ReportWind();
            ReportSporeBombs();
            ReportSporeAreas();
            ReportCreatures();
            Diag.Info("===== end diagnostics =====");
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

        private static void ProbeSporeBombStructure(Transform t, string kind)
        {
            var go = t.gameObject;
            Diag.Info($"[SporeBombs]   probe {kind} @ {Fmt(t.position)} name=\"{t.name}\"");
            Diag.Info($"[SporeBombs]     self components: {ComponentNames(go)}");

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

    /// <summary>
    /// Fires the auto scene scan after native prop placement finishes - the same
    /// postfix-on-<c>PropGrouper.RunAll</c> seam the real spore-bomb cull will
    /// eventually hook (RESEARCH.md Q5). Gated entirely on the debug toggle.
    /// </summary>
    [HarmonyPatch(typeof(PropGrouper), nameof(PropGrouper.RunAll))]
    internal static class PropGrouper_RunAll_Diagnostics
    {
        private static void Postfix()
        {
            if (!Diag.Enabled || Plugin.Cfg == null || !Plugin.Cfg.LogSceneScanOnLoad.Value)
            {
                return;
            }

            SceneDiagnostics.AutoDump("level generated (PropGrouper.RunAll)");
        }
    }
}
