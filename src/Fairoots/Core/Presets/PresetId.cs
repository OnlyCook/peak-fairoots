namespace Fairoots.Core.Presets
{
    /// <summary>
    /// The four presets, lightest touch (1) to heaviest (4). Preset 2 (Balanced)
    /// is the default - the tuning the maintainer would ship "as the base game's
    /// own balance pass" (ROADMAP.md "Presets"). The numeric backing values ordered
    /// by this scale live in <see cref="PresetCatalog"/>.
    /// </summary>
    public enum PresetId
    {
        Subtle = 1,
        Balanced = 2,
        Generous = 3,
        Tame = 4,
    }
}
