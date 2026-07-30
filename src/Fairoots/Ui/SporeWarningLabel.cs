using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using Fairoots.SporeBombs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fairoots.Ui
{
    /// <summary>
    /// An opt-in on-screen warning (<c>General/show-spore-cloud-label</c>, off by
    /// default) that says in words what the green screen overlay says in colour:
    /// you are standing in spores right now.
    ///
    /// <b>Why words, when there's already an overlay.</b> The overlay is a coloured
    /// tint, and a coloured tint is exactly the signal that competes with a coloured
    /// cloud - the ambiguity this whole feature set exists to remove. Text doesn't
    /// compete with anything in the scene, so it stays readable regardless of what
    /// the player is standing in or looking at. Off by default because it's a much
    /// louder intervention than the rest: the translucency and overlay settings make
    /// the game's own feedback legible, while this adds something the game never had.
    ///
    /// Covers <b>both</b> hazards (persistent spore areas and spore-bomb clouds) off
    /// the shared <see cref="SporePresence"/> query, so the label can never disagree
    /// with the overlay about whether the player is in danger.
    ///
    /// Client-side and cosmetic, like everything else in <c>General</c> that isn't
    /// the seed or the preset.
    ///
    /// <b>Look.</b> The game's own UI font and outlined text material, discovered at
    /// runtime (<see cref="NativeUiAssets"/>) rather than bundled, so it reads as
    /// part of PEAK's HUD. The text colour is the live Spores status colour - the
    /// same field the spore-bomb recolor reads, so the label, the status effect and
    /// the recoloured hazards all agree by construction rather than by three
    /// hand-picked hex values - over a darkened-but-same-hue outline
    /// (<see cref="LabelColors.Outline"/>), which is what keeps pink text legible
    /// against a pink cloud.
    /// </summary>
    internal static class SporeWarningLabel
    {
        /// <summary>
        /// Where the label sits, in reference-resolution pixels below the top edge.
        /// The canvas is 1920x1080-referenced and the crosshair is the screen centre,
        /// so 270 is the midpoint between the top of the screen and the crosshair -
        /// the maintainer's requested placement. Expressed against the top edge (not
        /// the centre) so it keeps that relationship at any aspect ratio.
        /// </summary>
        private const float TopOffsetPixels = 270f;

        /// <summary>Seconds the label takes to fade fully in or out. Short enough to feel immediate, long enough not to pop.</summary>
        private const float FadeSeconds = 0.15f;

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TextMeshProUGUI _text;

        /// <summary>Instanced copy of the game's outlined material, so the outline colour can be set without touching the shared asset every native label uses.</summary>
        private static Material _material;

        private static int _outlineColorId = -1;

        /// <summary>Last colour written, so the material isn't re-tinted every frame for no reason.</summary>
        private static Color _appliedColor;

        /// <summary>
        /// The language the current text was resolved for, so a language change made
        /// mid-session (the game's settings menu does it without a scene reload) is
        /// picked up instead of the label keeping whatever was active the frame it
        /// was built. Nullable so the first <see cref="ApplyText"/> always writes.
        /// </summary>
        private static LocalizedText.Language? _appliedLanguage;

        /// <summary>Polled from <c>Plugin.Update</c>.</summary>
        internal static void Tick()
        {
            try
            {
                bool wanted = Plugin.Cfg.ShowSporeCloudLabel.Value && SporePresence.InAnySpores();

                // Nothing built and nothing to show: the common case by far (the
                // setting is off by default), so it costs one bool and returns.
                if (!wanted && _root == null)
                {
                    return;
                }

                if (_root == null)
                {
                    if (!NativeUiAssets.TryResolve())
                    {
                        return; // no native font yet - try again next frame.
                    }

                    Build();
                }

                ApplyText();
                ApplyColor();
                Fade(wanted);
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeWarningLabel] Tick threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Build()
        {
            _root = new GameObject("FairootsSporeWarningLabel");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the game's own HUD, deliberately: this is a warning, and the one
            // thing worse than not having it is having it hidden behind something.
            canvas.sortingOrder = GUIManager.instance != null && GUIManager.instance.hudCanvas != null
                ? GUIManager.instance.hudCanvas.sortingOrder + 1
                : 100;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // No GraphicRaycaster at all: this label is never interactive, and one
            // that could take a click would sit invisibly over the game's own UI.
            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_root.transform, false);

            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 60f);
            rect.anchoredPosition = new Vector2(0f, -TopOffsetPixels);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.font = NativeUiAssets.Font;
            _text.fontSize = 40f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;

            // An instance, never the shared asset: writing the outline colour into
            // NativeUiAssets.OutlineMaterial would repaint the outline of every
            // native label in the game that uses it.
            _material = new Material(NativeUiAssets.OutlineMaterial);
            _text.fontSharedMaterial = _material;
            _outlineColorId = Shader.PropertyToID("_OutlineColor");

            Diag.Info("[SporeWarningLabel] built (native font + outlined material found)");
        }

        /// <summary>
        /// Writes the label's text in the game's current language
        /// (<see cref="WarningLabelLocalization"/>), and only when that language has
        /// actually changed - assigning <c>TextMeshProUGUI.text</c> forces a mesh
        /// rebuild, so doing it unconditionally every frame would be a per-frame
        /// re-layout for a string that almost never changes.
        /// </summary>
        private static void ApplyText()
        {
            LocalizedText.Language language = WarningLabelLocalization.CurrentLanguage;
            if (_appliedLanguage == language)
            {
                return;
            }

            _appliedLanguage = language;
            _text.text = WarningLabelLocalization.Get(WarningLabelKey.BreathingInSpores);
        }

        private static void ApplyColor()
        {
            Rgb spore = SporeBombRecolorPatch.ResolveSporeColor();
            var color = new Color((float)spore.R, (float)spore.G, (float)spore.B, 1f);
            if (color == _appliedColor)
            {
                return;
            }

            _appliedColor = color;
            _text.color = color;

            Rgb outline = LabelColors.Outline(spore);
            _material.SetColor(_outlineColorId, new Color((float)outline.R, (float)outline.G, (float)outline.B, 1f));
        }

        private static void Fade(bool visible)
        {
            float target = visible ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / FadeSeconds);

            // Disabled outright once fully faded out, so a label that isn't showing
            // costs nothing to render rather than a transparent full-canvas draw.
            bool active = _group.alpha > 0f;
            if (_root.activeSelf != active)
            {
                _root.SetActive(active);
            }
        }
    }
}
