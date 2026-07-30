using System.Collections.Generic;
using Fairoots.Networking;

namespace Fairoots.Ui
{
    /// <summary>
    /// Translations for the mod's on-screen labels: the two warnings
    /// (<see cref="SporeWarningLabel"/>, <see cref="SpiderWarningLabel"/>) and the
    /// Roots setup screen (<see cref="RootsLoadingOverlay"/>). The warnings used to
    /// be English-only by explicit scoping; live-reported 2026-07-30 as the one
    /// player-visible text in the mod that didn't follow the game's language setting,
    /// so they now land exactly the way
    /// <c>Networking/ModPresenceLocalization</c> does it.
    ///
    /// Array order matches <c>LocalizedText.Language</c>'s declaration order
    /// (English, French, Italian, German, SpanishSpain, SpanishLatam, BRPortuguese,
    /// Russian, Ukrainian, SimplifiedChinese, TraditionalChinese, Japanese, Korean,
    /// Polish, Turkish) - <see cref="LocalizationHelper"/> falls back to English
    /// (index 0) for any entry a language doesn't cover. TraditionalChinese is left
    /// blank, matching the convention already set by
    /// <c>ModPresenceLocalization</c>: the game's own
    /// <c>LocalizedText.LANGUAGE_COUNT</c> is 14, one less than the 15-value enum,
    /// so this mirrors that precedent rather than guessing a translation for a
    /// language slot the game itself doesn't use.
    ///
    /// Kept deliberately short in every language: these are read at a glance, in
    /// the moment, off a single unwrapped line
    /// (<c>enableWordWrapping = false</c> in both labels). No em dashes anywhere,
    /// per the maintainer's request.
    /// </summary>
    internal enum WarningLabelKey
    {
        /// <summary>The spore-presence warning: the player is standing in spores right now.</summary>
        BreathingInSpores,

        /// <summary>The spider strike warning: a spider is descending on the local player.</summary>
        SpiderDropping,

        /// <summary>The Roots setup screen: Fairoots is applying its per-level changes to a freshly-loaded biome.</summary>
        PreparingRoots,
    }

    internal static class WarningLabelLocalization
    {
        private static readonly Dictionary<WarningLabelKey, string[]> Table = new Dictionary<WarningLabelKey, string[]>
        {
            [WarningLabelKey.BreathingInSpores] = new[]
            {
                "Breathing in spores!",
                "Vous respirez des spores !",
                "Stai respirando spore!",
                "Du atmest Sporen ein!",
                "¡Estás respirando esporas!",
                "¡Estás respirando esporas!",
                "Você está respirando esporos!",
                "Вы дышите спорами!",
                "Ви дихаєте спорами!",
                "正在吸入孢子！",
                "",
                "胞子を吸い込んでいます！",
                "포자를 흡입하고 있습니다!",
                "Wdychasz zarodniki!",
                "Spor soluyorsun!",
            },
            [WarningLabelKey.SpiderDropping] = new[]
            {
                "Spider dropping on you!",
                "Une araignée descend sur vous !",
                "Un ragno ti sta cadendo addosso!",
                "Eine Spinne lässt sich auf dich herab!",
                "¡Una araña se descuelga sobre ti!",
                "¡Una araña se descuelga sobre ti!",
                "Uma aranha está descendo sobre você!",
                "На вас спускается паук!",
                "На вас спускається павук!",
                "蜘蛛正朝你落下！",
                "",
                "クモが降りてきます！",
                "거미가 당신을 향해 내려옵니다!",
                "Pająk spada na ciebie!",
                "Bir örümcek üstüne iniyor!",
            },
            [WarningLabelKey.PreparingRoots] = new[]
            {
                "Preparing the Roots...",
                "Préparation des Racines...",
                "Preparazione delle Radici...",
                "Die Wurzeln werden vorbereitet...",
                "Preparando las Raíces...",
                "Preparando las Raíces...",
                "Preparando as Raízes...",
                "Подготовка Корней...",
                "Підготовка Коренів...",
                "正在准备根系...",
                "",
                "ルーツを準備しています...",
                "뿌리를 준비하는 중...",
                "Przygotowywanie Korzeni...",
                "Kökler hazırlanıyor...",
            },
        };

        /// <summary>Text for the current value of <c>LocalizedText.CURRENT_LANGUAGE</c>.</summary>
        public static string Get(WarningLabelKey key) => LocalizationHelper.Resolve(Table[key]);

        /// <summary>
        /// The language the labels last resolved their text for. Both labels build
        /// their <c>TextMeshProUGUI</c> once and then only tick colour/alpha, so
        /// without re-checking this they would keep whatever language was active the
        /// frame they were built - and the language can be changed mid-session from
        /// the game's own settings menu, without a scene reload.
        /// </summary>
        public static LocalizedText.Language CurrentLanguage => LocalizedText.CURRENT_LANGUAGE;
    }
}
