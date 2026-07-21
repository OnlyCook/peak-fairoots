namespace Fairoots.Core.Presets
{
    /// <summary>
    /// The five presets. 1-4 are lightest touch to heaviest; Preset 2 (Balanced)
    /// is the default - the tuning the maintainer would ship "as the base game's
    /// own balance pass" (ROADMAP.md "Presets"). The numeric backing values ordered
    /// by that scale live in <see cref="PresetCatalog"/>.
    ///
    /// <see cref="Custom"/> (5) is not on that scale at all: it doesn't fold any
    /// hard-coded preset numbers in, it just means "use whatever the player has
    /// configured for every per-mechanic setting, directly." See
    /// <see cref="PresetCatalog"/>'s remarks for how that's implemented (a
    /// same-numbers-as-Balanced fallback used only for a setting the player never
    /// touched under Custom - not "following" a preset, just avoiding a broken
    /// sentinel value leaking through).
    /// </summary>
    public enum PresetId
    {
        Subtle = 1,
        Balanced = 2,
        Generous = 3,
        Tame = 4,
        Custom = 5,
    }
}
