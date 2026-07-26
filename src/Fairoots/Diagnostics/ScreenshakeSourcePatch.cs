using System;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Diagnostic-only (off unless <c>Debug/log-screenshake-sources</c> is on, on top
    /// of the master debug toggle): logs every shake queued on
    /// <c>PerlinShake.AddShake</c> - the single funnel every camera shake in the game
    /// ends up in, whether it came from <c>AddScreenshake</c>, an affliction FX, a
    /// fall RPC or anything else - together with the managed call stack that asked
    /// for it.
    ///
    /// Exists because "the screen still shakes for a detonation 200m away" can't be
    /// diagnosed from the spore-bomb side alone: if the shake isn't coming from the
    /// detonation's own <c>AddScreenshake</c>, no amount of capping that component
    /// will help, and this is what says where it *is* coming from. Ships permanently
    /// rather than as a throwaway because the same question will come up for the
    /// other mechanics.
    /// </summary>
    [HarmonyPatch]
    internal static class ScreenshakeSourcePatch
    {
        /// <summary>Frames of managed stack to print (past the patch's own frames).</summary>
        private const int StackFramesToLog = 8;

        [HarmonyPatch(typeof(PerlinShake), nameof(PerlinShake.AddShake),
            new Type[] { typeof(float), typeof(float), typeof(float) })]
        [HarmonyPrefix]
        private static void LogShake(float amount, float duration, float scale)
        {
            try
            {
                if (!Diag.Enabled || Plugin.Cfg == null || !Plugin.Cfg.LogScreenshakeSources.Value)
                {
                    return;
                }

                // A zero-amount shake is a distance cap doing its job (the proximity
                // helper multiplies the amount down to nothing rather than bailing
                // out), so it's the *interesting* non-event - log it, quietly.
                Diag.Info(
                    $"[Screenshake] amount={amount:0.###} duration={duration:0.##} scale={scale:0.#}" +
                    $"{(amount <= 0.0001f ? " (silent - fully attenuated)" : string.Empty)}\n{Caller()}");
            }
            catch (Exception e)
            {
                Diag.Error($"[Screenshake] tracer threw: {e.GetType().Name}: {e.Message}");
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

    /// <summary>
    /// Companion tracer on <c>GamefeelHandler.AddPerlinShakeProximity</c>, the
    /// distance-attenuated entry point. Logs the source position and how far the
    /// observed character actually was from it, so a shake that should have been
    /// attenuated to nothing but wasn't is immediately obvious.
    /// </summary>
    [HarmonyPatch(typeof(GamefeelHandler), nameof(GamefeelHandler.AddPerlinShakeProximity))]
    internal static class ProximityShakeSourcePatch
    {
        private static void Prefix(Vector3 position, float amount, float maxProximity)
        {
            try
            {
                if (!Diag.Enabled || Plugin.Cfg == null || !Plugin.Cfg.LogScreenshakeSources.Value)
                {
                    return;
                }

                var observed = Character.observedCharacter;
                string distance = observed == null
                    ? "observedCharacter=null (NO attenuation applied - full-strength shake)"
                    : $"distance={GameUnits.ToMeters(Vector3.Distance(observed.Center, position)):0.#}m";

                Diag.Info(
                    $"[Screenshake] proximity shake @ {position} amount={amount:0.##} " +
                    $"maxProximity={GameUnits.ToMeters(maxProximity):0.#}m, {distance}");
            }
            catch (Exception e)
            {
                Diag.Error($"[Screenshake] proximity tracer threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
