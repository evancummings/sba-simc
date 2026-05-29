namespace SbaSimc.Models;

/// <summary>
/// Represents a single simulatable combination of class, spec, and hero talent tree.
/// </summary>
public record WowSpec(
    string Class,
    string Spec,
    string HeroTalent,

    /// <summary>
    /// The base SimC profile filename (without .simc extension), e.g. "T33_Warrior_Arms".
    /// Corresponds to a file inside ContainerProfilesPath in the SimC container.
    /// </summary>
    string SimcProfileName,

    /// <summary>
    /// Free-form SimC options appended to every run for this spec (both optimal and Blizzard APL).
    /// Use this to specify hero talent selection, e.g. "hero_talents=slayer".
    /// NOTE: verify exact option names against the running container — see CLAUDE.md.
    /// </summary>
    string AdditionalSimcOptions = ""
);
