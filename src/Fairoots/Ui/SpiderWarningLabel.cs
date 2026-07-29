using System;
using Fairoots.Core;
using Fairoots.Creatures;
using Fairoots.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fairoots.Ui
{
    /// <summary>
    /// The spider strike indicator (<c>General/show-spider-warning-label</c>): an
    /// on-screen warning while a spider is descending on the local player. Built as a
    /// deliberate copy of <see cref="SporeWarningLabel"/>'s look and placement, at the
    /// maintainer's request, so the two read as one HUD language rather than two mods'
    /// worth of text.
    ///
    /// <b>Off by default</b>, like the spore label and every other setting in the group
    /// that adds a HUD element PEAK doesn't have - opted into, not out of. That's a
    /// consistency call rather than a comment on its usefulness: unlike the spore label,
    /// this one has no vanilla counterpart at all, since the game's only spider cue is
    /// the <c>webMovement</c> SFX, which plays in the same frame the drop begins, and
    /// the grab that follows is instant on contact with no windup
    /// (<c>SpiderTrigger.OnTriggerEnter</c>). A player not looking up gets nothing. See
    /// <see cref="SpiderStrikeWarning"/> for the full timing, including why the warning
    /// covers only the drop itself and not the spider's post-landing hang.
    ///
    /// <b>Colour.</b> The live Poison status colour, read off the same
    /// <c>CharacterAfflictions</c> the game tints the player with - poison being what a
    /// spider actually does to you once it has you. Same principle as the spore label
    /// keying off <c>colorSpores</c>: the warning and the status it warns about agree by
    /// construction rather than by a hand-picked hex value. There is no
    /// <c>colorWeb</c> in the build to use instead. Over a darkened-but-same-hue outline
    /// (<see cref="LabelColors.Outline"/>), written into an <b>instanced</b> copy of the
    /// native material - writing the shared asset would repaint the outline of every
    /// native label in the game.
    ///
    /// Per-client and cosmetic: it changes what one player sees on their own screen and
    /// nothing about anyone else's game, so it's deliberately not host-authoritative -
    /// the same call already made for <c>recolor-spore-bombs</c>.
    /// </summary>
    internal static class SpiderWarningLabel
    {
        /// <summary>
        /// English only for now, matching <see cref="SporeWarningLabel"/>'s scoping -
        /// localization lands later for both at once, the way
        /// <c>Networking/ModPresenceLocalization</c> does it.
        /// </summary>
        private const string WarningText = "Spider dropping on you!";

        /// <summary>
        /// Sits below <see cref="SporeWarningLabel"/>'s 270px so the two can be on
        /// screen together - being spored and being jumped on are entirely
        /// independent, and one warning silently covering the other would be worse
        /// than either alone.
        /// </summary>
        private const float TopOffsetPixels = 330f;

        private const float FadeSeconds = 0.12f;

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TextMeshProUGUI _text;
        private static Material _material;
        private static int _outlineColorId = -1;
        private static Color _appliedColor;

        /// <summary>Cached live Poison status colour; resolved once, like the spore label's.</summary>
        private static Rgb? _poisonColor;

        /// <summary>
        /// Fallback if the live colour can't be read (no local character yet). A purple
        /// close to PEAK's own poison tint - only ever used for the frames before the
        /// real value is available.
        /// </summary>
        private static readonly Rgb FallbackPoisonColor = new Rgb(0.62, 0.31, 0.78);

        /// <summary>Polled from <c>Plugin.Update</c>.</summary>
        internal static void Tick()
        {
            try
            {
                bool wanted = Plugin.Cfg.ShowSpiderWarningLabel.Value && SpiderStrikeWarning.StrikeIncoming();

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

                ApplyColor();
                Fade(wanted);
            }
            catch (Exception e)
            {
                Diag.Error($"[SpiderWarningLabel] Tick threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Build()
        {
            _root = new GameObject("FairootsSpiderWarningLabel");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = GUIManager.instance != null && GUIManager.instance.hudCanvas != null
                ? GUIManager.instance.hudCanvas.sortingOrder + 1
                : 100;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

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
            _text.text = WarningText;
            _text.fontSize = 40f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;

            _material = new Material(NativeUiAssets.OutlineMaterial);
            _text.fontSharedMaterial = _material;
            _outlineColorId = Shader.PropertyToID("_OutlineColor");

            Diag.Info("[SpiderWarningLabel] built (native font + outlined material found)");
        }

        private static void ApplyColor()
        {
            Rgb poison = ResolvePoisonColor();
            var color = new Color((float)poison.R, (float)poison.G, (float)poison.B, 1f);
            if (color == _appliedColor)
            {
                return;
            }

            _appliedColor = color;
            _text.color = color;

            Rgb outline = LabelColors.Outline(poison);
            _material.SetColor(_outlineColorId, new Color((float)outline.R, (float)outline.G, (float)outline.B, 1f));
        }

        private static Rgb ResolvePoisonColor()
        {
            if (_poisonColor.HasValue)
            {
                return _poisonColor.Value;
            }

            try
            {
                var character = Character.localCharacter;
                var afflictions = character != null && character.refs != null ? character.refs.afflictions : null;
                if (afflictions != null && afflictions.colorPoison.maxColorComponent > 0f)
                {
                    Color live = afflictions.colorPoison;
                    var rgb = new Rgb(live.r, live.g, live.b);
                    _poisonColor = rgb;
                    Diag.Info($"[SpiderWarningLabel] read live Poison status color {rgb}");
                    return rgb;
                }
            }
            catch (Exception e)
            {
                Diag.V($"[SpiderWarningLabel] could not read the live Poison status color ({e.GetType().Name}) - using the fallback");
            }

            return FallbackPoisonColor;
        }

        private static void Fade(bool visible)
        {
            float target = visible ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / FadeSeconds);

            bool active = _group.alpha > 0f;
            if (_root.activeSelf != active)
            {
                _root.SetActive(active);
            }
        }
    }
}
