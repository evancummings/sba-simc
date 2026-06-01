namespace SbaSimc.Models;

/// <summary>
/// All fight-profile results for one spec/hero-talent row on the index and detail pages.
/// </summary>
public record SpecResultBundle(
    WowSpec Spec,
    string DetailSlug,
    bool HasDetailPage,
    IReadOnlyDictionary<string, SimulationResult> ResultsByProfile,
    IReadOnlyDictionary<string, SpecDetail> DetailsByProfile
);
