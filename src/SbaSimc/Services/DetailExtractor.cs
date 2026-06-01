using System.Text.Json;
using System.Text.Json.Nodes;
using SbaSimc.Models;

namespace SbaSimc.Services;

public static class DetailExtractor
{
    private const int MaxTimelinePoints = 120;
    private const int MaxSampleActions = 120;
    private const double MaxSampleTimeSec = 60.0;

    public static SpecDetail? Extract(WowSpec spec, string optimalJson, string ahJson, string obJson, int iterations)
    {
        var simc = ExtractMode(optimalJson);
        var ah = ExtractMode(ahJson);
        var ob = ExtractMode(obJson);

        if (simc is null || ah is null || ob is null)
            return null;

        var root = JsonNode.Parse(optimalJson);
        var player = root?["sim"]?["players"]?[0];
        var options = root?["sim"]?["options"];

        var abilities = MergeAbilities(simc, ah, ob);
        var slug = SpecDetail.SlugFrom(spec);

        return new SpecDetail(
            Slug: slug,
            Class: spec.Class,
            Spec: spec.Spec,
            HeroTalent: spec.HeroTalent,
            SimcProfileName: spec.SimcProfileName,
            PlayerName: player?["name"]?.GetValue<string>() ?? spec.SimcProfileName,
            Talents: player?["talents"]?.GetValue<string>(),
            Simc: simc,
            AssistedHighlight: ah,
            OneButton: ob,
            Abilities: abilities,
            FightStyle: options?["fight_style"]?.GetValue<string>() ?? "Patchwerk",
            Iterations: iterations
        );
    }

    private static SimModeDetail? ExtractMode(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var player = root?["sim"]?["players"]?[0];
            var collected = player?["collected_data"];
            if (player is null || collected is null)
                return null;

            var dps = collected["dps"]?["mean"]?.GetValue<double>();
            var fightLength = collected["fight_length"]?["mean"]?.GetValue<double>() ?? 300.0;
            if (dps is null)
                return null;

            var abilities = ExtractAbilities(player["stats"]?.AsArray());
            var timeline = DownsampleTimeline(collected["timeline_dmg"]?["data"]?.AsArray(), fightLength);
            var sampleActions = ExtractSampleActions(collected["action_sequence"]?.AsArray());
            var profileSource = player["profile_source"]?.GetValue<string>();

            return new SimModeDetail(
                Dps: dps.Value,
                FightLengthSec: fightLength,
                ProfileSource: profileSource,
                Timeline: timeline,
                SampleActions: sampleActions,
                AbilitiesByKey: abilities
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    ✗ Failed to extract detail data: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, AbilityStat> ExtractAbilities(JsonArray? stats)
    {
        var result = new Dictionary<string, AbilityStat>(StringComparer.OrdinalIgnoreCase);
        if (stats is null)
            return result;

        foreach (var node in stats)
        {
            if (node is null)
                continue;

            var totalAmount = node["total_amount"]?["mean"]?.GetValue<double>();
            if (totalAmount is null or <= 0)
                continue;

            var key = node["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var spellName = node["spell_name"]?.GetValue<string>() ?? key;
            var portion = node["portion_amount"]?.GetValue<double>() ?? 0;
            var executes = node["num_executes"]?["mean"]?.GetValue<double>() ?? 0;
            var interval = node["total_intervals"]?["mean"]?.GetValue<double>();
            var critPercent = ExtractCritPercent(node["direct_results"] as JsonObject);
            var avgHit = executes > 0 ? totalAmount.Value / executes : 0;

            result[key] = new AbilityStat(
                Name: key,
                SpellName: spellName,
                PortionPercent: portion * 100.0,
                ExecutesPerFight: executes,
                IntervalSec: interval,
                CritPercent: critPercent,
                AvgHit: avgHit,
                TotalDamageMean: totalAmount.Value
            );
        }

        return result;
    }

    private static double? ExtractCritPercent(JsonObject? directResults)
    {
        if (directResults is null)
            return null;

        var critCount = directResults["crit"]?["actual_amount"]?["count"]?.GetValue<double>() ?? 0;
        var hitCount = directResults["hit"]?["actual_amount"]?["count"]?.GetValue<double>() ?? 0;
        var total = critCount + hitCount;
        return total > 0 ? critCount / total * 100.0 : null;
    }

    private static IReadOnlyList<double> DownsampleTimeline(JsonArray? data, double fightLengthSec)
    {
        if (data is null || data.Count == 0)
            return Array.Empty<double>();

        var values = data.Select(n => n?.GetValue<double>() ?? 0).ToList();
        if (values.Count <= MaxTimelinePoints)
            return values;

        var step = (double)values.Count / MaxTimelinePoints;
        var sampled = new List<double>(MaxTimelinePoints);
        for (var i = 0; i < MaxTimelinePoints; i++)
        {
            var idx = (int)Math.Floor(i * step);
            if (idx >= values.Count)
                idx = values.Count - 1;
            sampled.Add(values[idx]);
        }

        return sampled;
    }

    private static IReadOnlyList<SampleAction> ExtractSampleActions(JsonArray? sequence)
    {
        if (sequence is null)
            return Array.Empty<SampleAction>();

        var actions = new List<SampleAction>(MaxSampleActions);
        foreach (var node in sequence)
        {
            if (node is null)
                continue;

            var time = node["time"]?.GetValue<double>() ?? 0;
            if (time > MaxSampleTimeSec)
                break;

            var name = node["name"]?.GetValue<string>() ?? "unknown";
            var spellName = node["spell_name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(spellName))
                spellName = name == "wait" ? "Wait" : name;

            actions.Add(new SampleAction(
                TimeSec: time,
                Name: name,
                SpellName: spellName,
                Target: node["target"]?.GetValue<string>() ?? ""
            ));

            if (actions.Count >= MaxSampleActions)
                break;
        }

        return actions;
    }

    private static IReadOnlyList<AbilityRow> MergeAbilities(
        SimModeDetail simc,
        SimModeDetail ah,
        SimModeDetail ob)
    {
        var keys = simc.AbilitiesByKey.Keys
            .Union(ah.AbilitiesByKey.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(ob.AbilitiesByKey.Keys, StringComparer.OrdinalIgnoreCase);

        return keys
            .Select(key =>
            {
                simc.AbilitiesByKey.TryGetValue(key, out var s);
                ah.AbilitiesByKey.TryGetValue(key, out var a);
                ob.AbilitiesByKey.TryGetValue(key, out var o);

                var display = s?.SpellName ?? a?.SpellName ?? o?.SpellName ?? key;
                return new AbilityRow(key, display, s, a, o);
            })
            .OrderByDescending(r => r.SortPortion)
            .ToList();
    }

    public static string SerializeForEmbed(SpecDetail detail) =>
        JsonSerializer.Serialize(detail, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        });
}
