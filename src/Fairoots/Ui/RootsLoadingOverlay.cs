using System;
using Fairoots.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fairoots.Ui
{
    /// <summary>
    /// The dimmed "preparing the Roots..." screen shown while
    /// <see cref="RootsLevelWatcher"/> runs its per-level setup passes.
    ///
    /// <b>Why it exists.</b> Fairoots does a burst of one-shot work the moment the
    /// Roots biome appears - the seeded spore-bomb cull walks 400+ candidates and
    /// every foliage mesh vertex in the level, and half a dozen other passes sweep the
    /// segment behind it. That is a real, unavoidable stall (live-reported as a huge
    /// stutter as the biome loads in after lighting the campfire): the work has to
    /// finish before the player can be allowed to walk into a spore bomb the cull was
    /// about to remove, so it can't be spread out over gameplay. What it can be is
    /// <em>honest</em> - a loading screen turns a freeze into a wait, which is what
    /// this is. The setup runner puts it up, lets it render, and only then starts the
    /// heavy passes.
    ///
    /// <b>Deliberately minimal</b> - a full-screen dim plus one line of centered text,
    /// no procedural sprites, no layout - copied in shape from
    /// <c>peak-checkpoint-save</c>'s save-picker first-open loading indicator (same
    /// maintainer, same look), down to its dim colour, so the two mods' loading beats
    /// read as one thing rather than two. The text uses the game's own font and
    /// outlined material (<see cref="NativeUiAssets"/>) like this mod's warning
    /// labels, and is localized (<see cref="WarningLabelLocalization"/>).
    ///
    /// <b>Sorting order sits below the checkpoint mod's own loading screen</b>
    /// (32000): during a Quick Resume into a Roots campfire both are up at once, and
    /// the right thing to show is that mod's "LOADING SAVE..." screen, not two stacked
    /// overlays. Ours is what the player sees when Roots is entered by ordinary play,
    /// where nothing else has a screen up.
    /// </summary>
    internal static class RootsLoadingOverlay
    {
        /// <summary>Matches <c>peak-checkpoint-save</c>'s <c>SavePicker.DimColor</c> exactly - see the class remarks.</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.78f);

        /// <summary>
        /// Below the checkpoint mod's loading screen (32000) and its message overlay
        /// (31000), above the game's own HUD - see the class remarks.
        /// </summary>
        private const int SortingOrder = 30500;

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TextMeshProUGUI _text;
        private static Material _material;

        /// <summary>Whether the overlay object exists (it's destroyed once faded out).</summary>
        internal static bool Exists => _root != null;

        /// <summary>
        /// Builds and shows the overlay at zero alpha. Safe to call when the native
        /// font hasn't been found yet - the dim still shows, just without the label,
        /// which is a better failure than no feedback at all.
        /// </summary>
        internal static void Show()
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }

                _root.SetActive(true);
                ApplyText();
                _group.alpha = 0f;
            }
            catch (Exception e)
            {
                Diag.Error($"[RootsLoadingOverlay] Show threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Moves the overlay's alpha towards <paramref name="target"/> at
        /// <paramref name="perSecond"/>, and reports whether it has arrived. Driven a
        /// frame at a time by the setup runner rather than by a coroutine of its own,
        /// so the fade and the setup passes share one timeline (see
        /// <c>RootsLevelWatcher</c>).
        /// </summary>
        internal static bool Fade(float target, float perSecond)
        {
            if (_group == null)
            {
                return true;
            }

            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * perSecond);
            return Mathf.Approximately(_group.alpha, target);
        }

        /// <summary>Tears the overlay down. Idempotent - safe on a level exit that never showed one.</summary>
        internal static void Hide()
        {
            if (_root == null)
            {
                return;
            }

            try
            {
                UnityEngine.Object.Destroy(_root);
            }
            catch (Exception e)
            {
                Diag.Error($"[RootsLoadingOverlay] Hide threw: {e.GetType().Name}: {e.Message}");
            }

            _root = null;
            _group = null;
            _text = null;
            _material = null;
        }

        private static void Build()
        {
            _root = new GameObject("FairootsRootsLoadingOverlay");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Height-matched rather than the default 0.5 blend, the same fix (and for
            // the same reason) as peak-checkpoint-save's ApplyWidescreenScaler: on an
            // ultrawide monitor a blended match drags the canvas's effective vertical
            // reference height down, which would shrink this label off-centre.
            scaler.matchWidthOrHeight = 1f;

            // No GraphicRaycaster: the overlay is never interactive, and one that could
            // swallow a click would be a live input-blocker sitting over the game.
            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var dimGo = new GameObject("Dim", typeof(RectTransform));
            dimGo.transform.SetParent(_root.transform, false);
            var dim = dimGo.AddComponent<Image>();
            dim.color = DimColor;
            dim.raycastTarget = false;
            StretchFull((RectTransform)dimGo.transform);

            if (!NativeUiAssets.TryResolve())
            {
                Diag.Warn("[RootsLoadingOverlay] no native font found yet - showing the dim without a label.");
                return;
            }

            var textGo = new GameObject("LoadingText", typeof(RectTransform));
            textGo.transform.SetParent(_root.transform, false);
            StretchFull((RectTransform)textGo.transform);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.font = NativeUiAssets.Font;
            _text.fontSize = 44f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.color = new Color(0.98f, 0.99f, 1f);

            // An instance of the shared outlined material, never the asset itself -
            // same rule as SporeWarningLabel: writing to the shared one would repaint
            // every native label in the game.
            _material = new Material(NativeUiAssets.OutlineMaterial);
            _text.fontSharedMaterial = _material;
        }

        /// <summary>
        /// Resolves the label text every time the overlay is shown rather than once at
        /// build time - the game's language can be changed mid-session from its own
        /// settings menu, and this object survives between Roots loads.
        /// </summary>
        private static void ApplyText()
        {
            if (_text != null)
            {
                _text.text = WarningLabelLocalization.Get(WarningLabelKey.PreparingRoots);
            }
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
