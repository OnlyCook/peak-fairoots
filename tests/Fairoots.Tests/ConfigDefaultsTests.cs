using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Guards the rule <c>docs/PRESETS.md</c> exists to enforce: <b>every config
    /// default is the vanilla value</b>, so installing Fairoots, selecting the
    /// Custom preset and changing nothing plays exactly like unmodded PEAK.
    ///
    /// <see cref="ConfigDefaults"/> is generated from that table, so this test is
    /// deliberately an <em>independent restatement</em> of what "vanilla" means for
    /// each setting rather than a re-read of the table - the whole point is to fail
    /// when a cell in the Default column is edited to something that isn't vanilla,
    /// which a generated-file check could never notice. If a row here needs
    /// changing, that is the conversation the failure is asking for.
    ///
    /// One category is exempt, documented in the table: gated parameters - a dial
    /// that means nothing until the mechanic it belongs to is switched on, since
    /// vanilla has no value for the cost of a mechanic it doesn't have.
    /// <see cref="ConfigDefaults"/> covers the five gameplay sections only;
    /// <c>General</c> and <c>Debug</c> hold no balance values and aren't generated
    /// at all, so nothing from either reaches this test.
    ///
    /// Anything neither listed nor exempt fails <see cref="EveryDefaultIsClassified"/>,
    /// so a newly added setting can't slip past this file unnoticed.
    /// </summary>
    public class ConfigDefaultsTests
    {
        /// <summary>
        /// What vanilla means for each gameplay setting, restated by hand: 1.0 for a
        /// multiplier, 0 for a removal fraction or a duration the game doesn't have,
        /// false for a mechanic vanilla lacks.
        /// </summary>
        private static readonly Dictionary<string, object> VanillaValue = new Dictionary<string, object>
        {
            // Spore bombs.
            { "SporeBombCullFraction", 0.0 },
            { "EnableFoliageRemoval", false },
            { "SporeBombTriggerRadiusMultiplier", 1.0 },
            { "SporeBombKnockbackMultiplier", 1.0 },
            // 0 = uncapped, i.e. SporeBombExplosionTuning.NoScreenshakeCap.
            { "SporeBombScreenshakeRangeCapMeters", 0.0 },
            { "SporeBombVfxCountMultiplier", 1.0 },
            // 1.0 disables the height cutoff outright rather than scaling it.
            { "SporeBombTriggerHeightMultiplier", 1.0 },
            { "SporeBombSporeAreaRadiusMultiplier", 1.0 },
            { "CoverMouthBlocksSporeBombs", false },

            // Spore areas.
            { "DisableSporeAreas", false },
            { "SporeAreaRemovalFraction", 0.0 },
            { "SporeAreaRadiusMultiplier", 1.0 },
            { "SporeAreaStatusRateMultiplier", 1.0 },
            { "EnableCoverMouth", false },

            // The Spores status itself.
            { "SporeClearTimeMultiplier", 1.0 },
            { "SporeBuildUpMultiplier", 1.0 },

            // Creatures.
            { "DisableZombies", false },
            { "DisableBeetles", false },
            { "DisableSpiders", false },
            { "ZombieSpeedMultiplier", 1.0 },
            { "BeetleSpeedMultiplier", 1.0 },
            { "BeetleKnockbackMultiplier", 1.0 },
            { "CreatureRagdollMultiplier", 1.0 },
            // Vanilla zombies never deaggro at all, which is the "off" here; the
            // multiplier's 1.0 is the toughest setting, not vanilla (see
            // ZombieDeaggro), and is what a hand-enabled Custom run should start from.
            { "ZombieDeaggroEnabled", false },
            { "ZombieDeaggroMultiplier", 1.0 },
            { "BeetleDeaggroMultiplier", 1.0 },
            { "ZombieKnockoutSeconds", 0.0 },
            { "BeetleKnockoutSeconds", 0.0 },
            { "BlowgunAffectsCreatures", false },
            // 1.0 = vanilla and already nonzero (bots take 0.6x a player's push);
            // 0 = vanilla for beetles, which the game makes wind-immune outright.
            { "ZombieWindMultiplier", 1.0 },
            { "BeetleWindSusceptibility", 0.0 },

            // Wind.
            { "DisableWindEntirely", false },
            { "WindBackpackAlwaysImmune", false },
            { "WindForceMultiplier", 1.0 },
            { "WindGustDurationMultiplier", 1.0 },
            { "WindItemForceMultiplier", 1.0 },
            { "WindObstacleOcclusionRangeMultiplier", 1.0 },
            { "WindFallCameraDampenClamp", 0.0 },
            { "PreventWindRagdoll", false },
            { "ClimbSheltersFromWind", false },
            { "ClimbWindSpeedMultiplier", 1.0 },
            { "ClimbWindUpwardSpeedMultiplier", 1.0 },
            { "ClimbWindIntoWindSpeedMultiplier", 1.0 },
            { "ClimbWindGraceForceMultiplier", 1.0 },
        };

        /// <summary>
        /// Gated parameters: each only means anything while the setting named here is
        /// on, and every one of those parents is itself off (or zero) by default, so
        /// an untouched Custom preset is still exactly vanilla despite these carrying
        /// tuned numbers.
        /// </summary>
        private static readonly Dictionary<string, string> GatedBy = new Dictionary<string, string>
        {
            { "CoverMouthStaminaPerSecond", "EnableCoverMouth" },
            { "CreatureKnockoutMinThrowSpeed", "ZombieKnockoutSeconds" },
            { "CreatureKnockoutMaxThrowDistance", "ZombieKnockoutSeconds" },
            { "BlowgunCreatureStunSeconds", "BlowgunAffectsCreatures" },
            { "WindRecentForceWindowSeconds", "PreventWindRagdoll" },
            { "ClimbShelterGraceSeconds", "ClimbSheltersFromWind" },
        };

        private static IEnumerable<FieldInfo> Defaults() =>
            typeof(ConfigDefaults)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral);

        [Fact]
        public void EveryGameplayDefaultIsTheVanillaValue()
        {
            foreach (FieldInfo field in Defaults())
            {
                if (!VanillaValue.TryGetValue(field.Name, out object expected))
                {
                    continue;
                }

                object actual = field.GetRawConstantValue();
                if (expected is bool expectedBool)
                {
                    Assert.Equal(expectedBool, (bool)actual);
                }
                else
                {
                    Assert.Equal(
                        Convert.ToDouble(expected),
                        Convert.ToDouble(actual),
                        precision: 9);
                }
            }
        }

        [Fact]
        public void EveryDefaultIsClassified()
        {
            var unclassified = Defaults()
                .Select(f => f.Name)
                .Where(n => !VanillaValue.ContainsKey(n) && !GatedBy.ContainsKey(n))
                .ToList();

            Assert.True(
                unclassified.Count == 0,
                "New settings in docs/PRESETS.md that this test doesn't know about: "
                + string.Join(", ", unclassified)
                + ". Add each to VanillaValue with its vanilla value, or - only if it's a "
                + "gated parameter - to GatedBy naming the setting that gates it.");
        }

        [Fact]
        public void EveryGatedParameterHasAnOffByDefaultParent()
        {
            foreach (KeyValuePair<string, string> pair in GatedBy)
            {
                Assert.True(
                    VanillaValue.ContainsKey(pair.Value),
                    $"{pair.Key} claims to be gated by {pair.Value}, which isn't a vanilla-defaulted setting");

                object parent = VanillaValue[pair.Value];
                bool parentIsOff = parent is bool b ? !b : Convert.ToDouble(parent) == 0.0;
                Assert.True(
                    parentIsOff,
                    $"{pair.Key} is only exempt from the vanilla-default rule because {pair.Value} "
                    + "defaults to off/zero - it no longer does, so this default is now reachable "
                    + "on an untouched Custom preset and has to become the vanilla value.");
            }
        }

        [Fact]
        public void GatedParametersAreNotZeroed()
        {
            // The flip side: a gated parameter carries a tuned number precisely
            // because it is unreachable by default. One sitting at 0 would silently
            // break its mechanic the moment a player switched the parent on (a free
            // mouth cover, a knockout at any range, a zero-length grace window).
            foreach (string name in GatedBy.Keys)
            {
                FieldInfo field = Defaults().Single(f => f.Name == name);
                Assert.True(
                    Convert.ToDouble(field.GetRawConstantValue()) > 0.0,
                    $"{name} is a gated parameter and must ship a usable value, not 0");
            }
        }

        [Fact]
        public void PresetValuesRejectsCustom()
        {
            // PresetValues is indexed by presets 1-4 only; PresetCatalog is what
            // maps Custom to a safe key. A caller reaching past the catalog should
            // fail loudly rather than get Subtle's number by accident.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PresetValues.WindForceMultiplier(PresetId.Custom));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PresetValues.EnableCoverMouth(PresetId.Custom));
        }

        [Fact]
        public void EveryPresetDrivenSettingActuallyDiffersFromVanillaSomewhere()
        {
            // A preset-driven setting whose four columns are all the vanilla value
            // is a dial no preset uses - fine deliberately (build-up-multiplier and
            // the spore-bomb spore-area radius are documented as exactly that), but
            // worth pinning so the list can't grow silently: an unfinished row is
            // otherwise indistinguishable from an intentional one.
            var deliberatelyVanillaEverywhere = new HashSet<string>
            {
                "SporeBuildUpMultiplier",              // compounds with the per-hazard rows
                "SporeBombSporeAreaRadiusMultiplier",  // not yet tuned per preset
                "CoverMouthBlocksSporeBombs",          // off everywhere, for now
            };

            var presets = new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame };
            var vanillaEverywhere = new List<string>();

            foreach (MethodInfo method in typeof(PresetValues)
                .GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!VanillaValue.TryGetValue(method.Name, out object vanilla))
                {
                    continue;
                }

                bool differsSomewhere = presets.Any(p =>
                {
                    object value = method.Invoke(null, new object[] { p });
                    return vanilla is bool vb
                        ? (bool)value != vb
                        : Math.Abs(Convert.ToDouble(value) - Convert.ToDouble(vanilla)) > 1e-9;
                });

                if (!differsSomewhere)
                {
                    vanillaEverywhere.Add(method.Name);
                }
            }

            Assert.Equal(
                deliberatelyVanillaEverywhere.OrderBy(n => n),
                vanillaEverywhere.OrderBy(n => n));
        }
    }
}
