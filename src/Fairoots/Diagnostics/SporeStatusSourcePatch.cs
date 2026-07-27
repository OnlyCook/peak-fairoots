using System;
using System.Diagnostics;
using System.Text;
using Fairoots.SporeAreas;
using HarmonyLib;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Diagnostic-only (needs the master debug toggle): logs every Spores status
    /// application that lands on the local player <em>while their mouth is covered</em>,
    /// with the managed call stack that asked for it.
    ///
    /// Exists because the cover-mouth mechanic blocks the two spore sources anyone can
    /// find by reading the game's code - a spore area's <c>StatusEmitter</c> and a spore
    /// bomb's spawned <c>AOE</c> - and yet spores still arrive after a bomb goes off
    /// (live-reported 2026-07-27). The log already ruled out the obvious suspect: the
    /// AOE suppression fires, and no lingering <c>StatusEmitter</c> is ever created by a
    /// detonation. So the remaining source isn't something to be guessed at from the
    /// decompile - it has to be caught in the act, which is what this does. Same
    /// approach, and the same reasoning, as <see cref="ScreenshakeSourcePatch"/>.
    ///
    /// Deliberately narrow: only Spores, only the local character, only while covering.
    /// That combination should be *impossible*, so every line it prints is a bug report.
    /// </summary>
    [HarmonyPatch]
    internal static class SporeStatusSourcePatch
    {
        /// <summary>Frames of managed stack to print (past the patch's own frames).</summary>
        private const int StackFramesToLog = 10;

        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
        [HarmonyPrefix]
        private static void LogSporeStatus(
            CharacterAfflictions __instance,
            CharacterAfflictions.STATUSTYPE statusType,
            float amount)
        {
            try
            {
                if (!Diag.Enabled
                    || statusType != CharacterAfflictions.STATUSTYPE.Spores
                    || amount <= 0f
                    || !CoverMouthController.LocalCovering)
                {
                    return;
                }

                var character = __instance.GetComponentInParent<Character>();
                if (character == null || character != Character.localCharacter)
                {
                    return;
                }

                Diag.Info($"[SporeSource] +{amount:0.####} Spores applied WHILE THE MOUTH IS COVERED\n{Caller()}");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeSource] tracer threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Caller()
        {
            var trace = new StackTrace(2, false);
            var sb = new StringBuilder();
            int logged = 0;
            for (int i = 0; i < trace.FrameCount && logged < StackFramesToLog; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method == null)
                {
                    continue;
                }

                string owner = method.DeclaringType?.FullName ?? "<unknown>";
                if (owner.StartsWith("Fairoots.", StringComparison.Ordinal)
                    || owner.StartsWith("HarmonyLib.", StringComparison.Ordinal)
                    || owner.StartsWith("MonoMod.", StringComparison.Ordinal))
                {
                    continue; // our own patch plumbing - noise.
                }

                sb.Append("    <- ").Append(owner).Append('.').Append(method.Name).Append('\n');
                logged++;
            }

            return sb.Length == 0 ? "    <- <no managed frames>" : sb.ToString().TrimEnd('\n');
        }
    }
}
