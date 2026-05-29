using System.Text.Json;
using System.Text.Json.Serialization;
using SbaSimc.Models;

namespace SbaSimc.Services;

/// <summary>
/// Loads the list of WoW spec/hero-talent combinations from specs.json.
///
/// specs.json is the authoritative source for what gets simulated. Update it when:
///   - A new patch adds or renames hero talent trees
///   - SimC updates its profile naming convention (e.g. tier prefix changes from T33 to T34)
///   - You need to add or remove specific combinations
/// </summary>
public static class SpecLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<IReadOnlyList<WowSpec>> LoadAsync(string specsFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(specsFilePath))
            throw new FileNotFoundException($"specs.json not found at: {specsFilePath}");

        var json = await File.ReadAllTextAsync(specsFilePath, ct);

        var rawSpecs = JsonSerializer.Deserialize<List<RawSpec>>(json, Options)
            ?? throw new InvalidOperationException("specs.json deserialised to null");

        return rawSpecs
            .Select(s => new WowSpec(s.Class, s.Spec, s.HeroTalent, s.SimcProfileName, s.AdditionalSimcOptions))
            .ToList();
    }

    // Private DTO to avoid polluting WowSpec with JSON ceremony
    private sealed class RawSpec
    {
        public string Class { get; set; } = "";
        public string Spec { get; set; } = "";
        public string HeroTalent { get; set; } = "";
        public string SimcProfileName { get; set; } = "";
        public string AdditionalSimcOptions { get; set; } = "";
    }
}
