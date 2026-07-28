using TMPro;
using UnityEngine;

namespace Fairoots.Ui
{
    /// <summary>
    /// Finds the game's own UI font and its outlined text material at runtime.
    ///
    /// They have to be discovered by scanning live objects rather than loaded by
    /// name: a font and a material are Unity <em>assets</em>, so nothing in the
    /// decompiled C# references them and there's no path to <c>Resources.Load</c>.
    /// The lookup keys off the material name the game's own outlined labels use
    /// (<c>DarumaDropOne-Regular SDF Outline</c>) and takes that label's font with
    /// it, which also guarantees the two match. Same technique, and the same key,
    /// as <c>peak-sense-of-direction</c>'s <c>Labels/NativeAssets</c> - and the same
    /// reason <c>peak-checkpoint-save</c>'s save picker hunts for a font instead of
    /// bundling one: reusing the game's own asset is what makes mod text look like
    /// it belongs to the game.
    ///
    /// Retried until it succeeds rather than resolved once at startup: no native
    /// label necessarily exists yet while the plugin is loading (the player may still
    /// be at the main menu), and a failed lookup is cheap.
    /// </summary>
    internal static class NativeUiAssets
    {
        /// <summary>The material name that identifies the game's outlined label style - see the class remarks.</summary>
        private const string OutlineMaterialName = "DarumaDropOne-Regular SDF Outline";

        internal static TMP_FontAsset Font { get; private set; }

        /// <summary>The game's outlined text material. Never written to - callers that need a custom outline colour instance a copy of it.</summary>
        internal static Material OutlineMaterial { get; private set; }

        internal static bool Ready => Font != null && OutlineMaterial != null;

        /// <summary>Attempts the lookup if it hasn't succeeded yet. Safe (and cheap) to call every frame.</summary>
        internal static bool TryResolve()
        {
            if (Ready)
            {
                return true;
            }

            foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                var material = text.materialForRendering;
                if (material != null && material.name.Contains(OutlineMaterialName))
                {
                    Font = text.font;
                    OutlineMaterial = material;
                    return true;
                }
            }

            return false;
        }
    }
}
