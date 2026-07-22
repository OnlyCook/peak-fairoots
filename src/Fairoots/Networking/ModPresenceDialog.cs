using System;
using Fairoots.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fairoots.Networking
{
    /// <summary>
    /// A minimal runtime-built popup (dim background + panel + title + body +
    /// "OK" button), shown once per newly-detected gap by
    /// <see cref="ModPresenceCheck"/> to warn that not everyone in the lobby
    /// has Fairoots installed. Deliberately generic/short - no player names
    /// (per the maintainer's request: with several missing players that could
    /// clip or bloat the dialog) - the specific names go to the log instead
    /// (<see cref="ModPresenceCheck"/> logs them via <see cref="Diag"/>).
    ///
    /// Built from plain uGUI primitives at runtime rather than reusing an
    /// existing native <c>MenuWindow</c> instance (e.g. the pause menu's own
    /// confirm dialog, which peak-checkpoint-save's <c>PauseMenuPatch</c>
    /// reuses) - that dialog only exists while the pause menu itself is open,
    /// but this needs to appear proactively during normal gameplay. Reuses the
    /// game's own font (<c>Resources.FindObjectsOfTypeAll&lt;TMP_FontAsset&gt;</c>)
    /// for visual consistency, same technique peak-checkpoint-save's
    /// <c>SavePicker.FindGameFont</c> uses.
    /// </summary>
    internal static class ModPresenceDialog
    {
        private static readonly string[] PreferredFontNames =
        {
            "DarumaDropOne-Regular SDF", "Pangolin-Regular SDF", "Montserrat-Medium SDF", "LiberationSans SDF",
        };

        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.09f, 0.96f);
        private static readonly Color TitleColor = new Color(0.95f, 0.85f, 0.4f);
        private static readonly Color BodyColor = new Color(0.92f, 0.92f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.2f, 0.22f, 1f);

        private static GameObject _root;

        /// <summary>True while the dialog is currently up - <see cref="ModPresenceCheck"/> uses this to avoid stacking duplicates.</summary>
        internal static bool IsOpen => _root != null;

        internal static void Show()
        {
            try
            {
                if (IsOpen)
                {
                    return; // already up - ModPresenceCheck only calls Show() for a newly-changed gap anyway.
                }

                var font = FindGameFont();

                _root = new GameObject("Fairoots_ModPresenceDialog", typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(_root);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;
                _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                ((CanvasScaler)_root.GetComponent<CanvasScaler>()).referenceResolution = new Vector2(1920, 1080);
                _root.AddComponent<GraphicRaycaster>();

                var dimGo = new GameObject("Dim", typeof(RectTransform));
                dimGo.transform.SetParent(_root.transform, false);
                var dim = dimGo.AddComponent<Image>();
                dim.color = DimColor;
                StretchFull((RectTransform)dimGo.transform);

                var panelGo = new GameObject("Panel", typeof(RectTransform));
                panelGo.transform.SetParent(_root.transform, false);
                var panelImage = panelGo.AddComponent<Image>();
                panelImage.color = PanelColor;
                var panelRect = (RectTransform)panelGo.transform;
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(560, 260);

                var title = MakeText(panelGo.transform, "Title", 26, FontStyles.Bold, TitleColor, TextAlignmentOptions.Center, font);
                title.text = ModPresenceLocalization.Get(ModPresenceMsgKey.DialogTitle);
                var titleRect = (RectTransform)title.transform;
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, -24f);
                titleRect.sizeDelta = new Vector2(-40f, 36f);

                var body = MakeText(panelGo.transform, "Body", 18, FontStyles.Normal, BodyColor, TextAlignmentOptions.TopLeft, font);
                body.text = ModPresenceLocalization.Get(ModPresenceMsgKey.DialogBody);
                body.textWrappingMode = TextWrappingModes.Normal;
                var bodyRect = (RectTransform)body.transform;
                bodyRect.anchorMin = new Vector2(0f, 0f);
                bodyRect.anchorMax = new Vector2(1f, 1f);
                bodyRect.offsetMin = new Vector2(28f, 64f);
                bodyRect.offsetMax = new Vector2(-28f, -66f);

                var buttonGo = new GameObject("OkButton", typeof(RectTransform));
                buttonGo.transform.SetParent(panelGo.transform, false);
                var buttonImage = buttonGo.AddComponent<Image>();
                buttonImage.color = ButtonColor;
                var button = buttonGo.AddComponent<Button>();
                var buttonRect = (RectTransform)buttonGo.transform;
                buttonRect.anchorMin = new Vector2(0.5f, 0f);
                buttonRect.anchorMax = new Vector2(0.5f, 0f);
                buttonRect.pivot = new Vector2(0.5f, 0f);
                buttonRect.anchoredPosition = new Vector2(0f, 20f);
                buttonRect.sizeDelta = new Vector2(140f, 40f);

                var buttonText = MakeText(buttonGo.transform, "Text", 18, FontStyles.Normal, BodyColor, TextAlignmentOptions.Center, font);
                buttonText.text = ModPresenceLocalization.Get(ModPresenceMsgKey.OkButton);
                StretchFull((RectTransform)buttonText.transform);

                button.onClick.AddListener(Close);
            }
            catch (Exception e)
            {
                Diag.Error($"[ModPresenceDialog] Show threw: {e.GetType().Name}: {e.Message}");
                Close();
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private static TextMeshProUGUI MakeText(
            Transform parent, string name, float size, FontStyles style, Color color, TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            if (font != null)
            {
                tmp.font = font;
            }

            return tmp;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_FontAsset FindGameFont()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                foreach (string name in PreferredFontNames)
                {
                    foreach (var f in all)
                    {
                        if (f != null && f.name == name)
                        {
                            return f;
                        }
                    }
                }

                return all.Length > 0 ? all[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
