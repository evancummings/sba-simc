using System.Text.Json.Nodes;

namespace SbaSimc.Services;

/// <summary>
/// Extracts DPS values from SimulationCraft's json2 output format.
///
/// The relevant path in the JSON tree is:
///   sim.players[0].collected_data.dps.mean
///
/// To inspect the full output structure:
///   docker run --rm simulationcraftorg/simc:latest T33_Warrior_Arms iterations=10 json2=/dev/stdout
/// </summary>
public static class ResultParser
{
    public static double? ExtractDps(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            return root?["sim"]?["players"]?[0]?["collected_data"]?["dps"]?["mean"]?.GetValue<double>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    ✗ Failed to parse SimC JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts the SimC build version string embedded in json2 output.
    /// Path: build_date or version at the root level.
    /// </summary>
    public static string? ExtractVersion(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            return root?["version"]?.GetValue<string>()
                ?? root?["build_date"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
