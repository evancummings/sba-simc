using System.Text.Json;
using Scriban;
using Scriban.Runtime;
using SbaSimc;
using SbaSimc.Models;

namespace SbaSimc.Services;

/// <summary>
/// Renders the static comparison website from simulation results using a Scriban template.
/// The template is resolved relative to the running assembly's directory, then falls back
/// to a "templates/" subdirectory of the current working directory.
/// </summary>
public class SiteGenerator(string outputDir)
{
    private const string DefaultFightProfileId = "patchwerk";

    public async Task GenerateAsync(
        IEnumerable<SimulationResult> results,
        IEnumerable<SpecDetail> details,
        IReadOnlyList<FightProfile> fightProfiles,
        string simcVersion,
        DockerImageInfo dockerImage,
        int iterations,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var bundles = BuildBundles(results, details, fightProfiles);
        await GenerateIndexAsync(bundles, fightProfiles, simcVersion, dockerImage, iterations, ct);
        await GenerateDetailPagesAsync(bundles, fightProfiles, simcVersion, dockerImage, iterations, ct);
    }

    private static List<SpecResultBundle> BuildBundles(
        IEnumerable<SimulationResult> results,
        IEnumerable<SpecDetail> details,
        IReadOnlyList<FightProfile> fightProfiles)
    {
        var profileIds = fightProfiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resultsBySlug = results
            .GroupBy(r => r.DetailSlug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.FightProfileId, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var detailsBySlug = details
            .GroupBy(d => d.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToDictionary(d => d.FightProfileId, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var allSlugs = resultsBySlug.Keys
            .Union(detailsBySlug.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return allSlugs
            .Select(slug =>
            {
                resultsBySlug.TryGetValue(slug, out var profileResults);
                detailsBySlug.TryGetValue(slug, out var profileDetails);

                profileResults ??= new Dictionary<string, SimulationResult>(StringComparer.OrdinalIgnoreCase);
                profileDetails ??= new Dictionary<string, SpecDetail>(StringComparer.OrdinalIgnoreCase);

                var firstResult = profileResults.Values.FirstOrDefault();
                var firstDetail = profileDetails.Values.FirstOrDefault();
                var spec = firstResult?.Spec
                    ?? new WowSpec(
                        firstDetail!.Class,
                        firstDetail.Spec,
                        firstDetail.HeroTalent,
                        firstDetail.SimcProfileName);

                var hasDetailPage = profileIds.All(id => profileDetails.ContainsKey(id));

                var resultsWithFlag = profileResults.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value with { HasDetailPage = hasDetailPage },
                    StringComparer.OrdinalIgnoreCase);

                return new SpecResultBundle(spec, slug, hasDetailPage, resultsWithFlag, profileDetails);
            })
            .OrderBy(b => b.Spec.Class)
            .ThenBy(b => b.Spec.Spec)
            .ThenBy(b => b.Spec.HeroTalent)
            .ToList();
    }

    private async Task GenerateIndexAsync(
        IReadOnlyList<SpecResultBundle> bundles,
        IReadOnlyList<FightProfile> fightProfiles,
        string simcVersion,
        DockerImageInfo dockerImage,
        int iterations,
        CancellationToken ct)
    {
        var templatePath = ResolveTemplatePath("index.html.sbn");
        var templateText = await File.ReadAllTextAsync(templatePath, ct);
        var template = Template.Parse(templateText);

        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Template parse errors: {string.Join("; ", template.Messages)}");

        var defaultProfileId = fightProfiles.FirstOrDefault(p => p.Id == DefaultFightProfileId)?.Id
            ?? fightProfiles[0].Id;

        var defaultResults = bundles
            .Select(b => b.ResultsByProfile.GetValueOrDefault(defaultProfileId))
            .Where(r => r is not null)
            .Cast<SimulationResult>()
            .ToList();

        var classes = bundles
            .Select(b => b.Spec.Class)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var indexJson = IndexPayloadBuilder.Serialize(bundles, fightProfiles);

        var scriptObject = new ScriptObject();
        scriptObject.Import(new
        {
            results = defaultResults,
            bundles,
            fight_profiles = fightProfiles,
            default_fight_profile_id = defaultProfileId,
            index_data_json = indexJson,
            classes,
            generated_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC",
            simc_version = simcVersion,
            docker_image = dockerImage.Tag,
            docker_digest = dockerImage.Digest,
            docker_digest_short = dockerImage.DigestShort,
            docker_image_id = dockerImage.ImageId,
            docker_image_label = dockerImage.DisplayLabel,
            docker_link_label = dockerImage.LinkLabel,
            docker_hub_url = dockerImage.HubUrl,
            iterations,
            total_specs = bundles.Count,
            broken_apl_count = defaultResults.Count(r => r.LikelyBrokenApl),
            good_count = defaultResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Good),
            moderate_count = defaultResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Moderate),
            poor_count = defaultResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Poor),
        });

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var html = await template.RenderAsync(context);
        var outputFile = Path.Combine(outputDir, "index.html");
        await File.WriteAllTextAsync(outputFile, html, ct);

        Console.WriteLine($"  ✓ Written → {outputFile}");
    }

    private async Task GenerateDetailPagesAsync(
        IReadOnlyList<SpecResultBundle> bundles,
        IReadOnlyList<FightProfile> fightProfiles,
        string simcVersion,
        DockerImageInfo dockerImage,
        int iterations,
        CancellationToken ct)
    {
        var pageBundles = bundles.Where(b => b.DetailsByProfile.Count > 0).ToList();
        if (pageBundles.Count == 0)
        {
            Console.WriteLine("  ⚠ No detail pages to generate.");
            return;
        }

        var detailsDir = Path.Combine(outputDir, "details");
        Directory.CreateDirectory(detailsDir);

        var templatePath = ResolveTemplatePath("detail.html.sbn");
        var templateText = await File.ReadAllTextAsync(templatePath, ct);
        var template = Template.Parse(templateText);

        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Detail template parse errors: {string.Join("; ", template.Messages)}");

        var defaultProfileId = fightProfiles.FirstOrDefault(p => p.Id == DefaultFightProfileId)?.Id
            ?? fightProfiles[0].Id;

        foreach (var bundle in pageBundles)
        {
            if (!bundle.ResultsByProfile.TryGetValue(defaultProfileId, out var defaultResult))
                defaultResult = bundle.ResultsByProfile.Values.First();

            var detailsJson = DetailPagePayloadBuilder.Serialize(bundle, fightProfiles);
            var resultsJson = DetailPagePayloadBuilder.SerializeResults(bundle);
            var fightProfilesJson = JsonSerializer.Serialize(
                fightProfiles.Select(p => new { p.Id, p.Label, p.Description }),
                DetailPagePayloadBuilder.JsonOptions);

            var scriptObject = new ScriptObject();
            scriptObject.Import(new
            {
                result = defaultResult,
                bundle,
                fight_profiles = fightProfiles,
                default_fight_profile_id = defaultProfileId,
                details_by_profile_json = detailsJson,
                results_by_profile_json = resultsJson,
                fight_profiles_json = fightProfilesJson,
                generated_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC",
                simc_version = simcVersion,
                docker_image = dockerImage.Tag,
                docker_digest_short = dockerImage.DigestShort,
                docker_image_label = dockerImage.DisplayLabel,
                docker_link_label = dockerImage.LinkLabel,
                docker_hub_url = dockerImage.HubUrl,
                iterations,
            });

            var context = new TemplateContext();
            context.PushGlobal(scriptObject);

            var html = await template.RenderAsync(context);
            var outputFile = Path.Combine(detailsDir, $"{bundle.DetailSlug}.html");
            await File.WriteAllTextAsync(outputFile, html, ct);
        }

        Console.WriteLine($"  ✓ Written → {detailsDir}/ ({pageBundles.Count} pages)");
    }

    private static string ResolveTemplatePath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "templates", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "templates", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "templates", fileName),
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"Could not find {fileName} template. Tried:\n{string.Join("\n", candidates)}");
    }
}

internal static class IndexPayloadBuilder
{
    public static string Serialize(IReadOnlyList<SpecResultBundle> bundles, IReadOnlyList<FightProfile> fightProfiles)
    {
        var payload = new
        {
            profiles = fightProfiles.Select(p => new { p.Id, p.Label, p.Description }).ToList(),
            summaries = fightProfiles.ToDictionary(
                p => p.Id,
                p => Summarize(bundles, p.Id),
                StringComparer.OrdinalIgnoreCase),
            rows = bundles.Select(b => new
            {
                slug = b.DetailSlug,
                @class = b.Spec.Class,
                spec = b.Spec.Spec,
                heroTalent = b.Spec.HeroTalent,
                hasDetailPage = b.HasDetailPage,
                byProfile = b.ResultsByProfile.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ToRowProfile(kvp.Value),
                    StringComparer.OrdinalIgnoreCase),
            }).ToList(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object Summarize(IReadOnlyList<SpecResultBundle> bundles, string profileId)
    {
        var results = bundles
            .Select(b => b.ResultsByProfile.GetValueOrDefault(profileId))
            .Where(r => r is not null)
            .Cast<SimulationResult>()
            .ToList();

        return new
        {
            total = results.Count,
            good = results.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Good),
            moderate = results.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Moderate),
            poor = results.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Poor),
            broken = results.Count(r => r.LikelyBrokenApl),
        };
    }

    private static object ToRowProfile(SimulationResult r) => new
    {
        optimalDps = r.OptimalDps,
        assistedHighlightDps = r.AssistedHighlightDps,
        oneButtonDps = r.OneButtonDps,
        deltaPercent = r.DeltaPercent,
        oneButtonDeltaPercent = r.OneButtonDeltaPercent,
        likelyBrokenApl = r.LikelyBrokenApl,
        severity = (int)r.Severity,
        oneButtonSeverity = (int)r.OneButtonSeverity,
    };

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

internal static class DetailPagePayloadBuilder
{
    public static string Serialize(SpecResultBundle bundle, IReadOnlyList<FightProfile> fightProfiles)
    {
        var map = fightProfiles
            .Where(p => bundle.DetailsByProfile.ContainsKey(p.Id))
            .ToDictionary(
                p => p.Id,
                p => bundle.DetailsByProfile[p.Id],
                StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        });
    }

    public static string SerializeResults(SpecResultBundle bundle)
    {
        var map = bundle.ResultsByProfile.ToDictionary(
            kvp => kvp.Key,
            kvp => new
            {
                optimalDps = kvp.Value.OptimalDps,
                assistedHighlightDps = kvp.Value.AssistedHighlightDps,
                oneButtonDps = kvp.Value.OneButtonDps,
                deltaPercent = kvp.Value.DeltaPercent,
                oneButtonDeltaPercent = kvp.Value.OneButtonDeltaPercent,
                likelyBrokenApl = kvp.Value.LikelyBrokenApl,
            },
            StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(map, JsonOptions);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
