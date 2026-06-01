using Scriban;
using Scriban.Runtime;
using SbaSimc.Models;

namespace SbaSimc.Services;

/// <summary>
/// Renders the static comparison website from simulation results using a Scriban template.
/// The template is resolved relative to the running assembly's directory, then falls back
/// to a "templates/" subdirectory of the current working directory.
/// </summary>
public class SiteGenerator(string outputDir)
{
    public async Task GenerateAsync(
        IEnumerable<SimulationResult> results,
        IEnumerable<SpecDetail> details,
        string simcVersion,
        DockerImageInfo dockerImage,
        int iterations,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var detailBySlug = details.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);

        var sortedResults = results
            .Select(r => r with { HasDetailPage = detailBySlug.ContainsKey(r.DetailSlug) })
            .OrderBy(r => r.Spec.Class)
            .ThenBy(r => r.Spec.Spec)
            .ThenBy(r => r.Spec.HeroTalent)
            .ToList();

        await GenerateIndexAsync(sortedResults, simcVersion, dockerImage, iterations, ct);
        await GenerateDetailPagesAsync(sortedResults, detailBySlug, simcVersion, dockerImage, iterations, ct);
    }

    private async Task GenerateIndexAsync(
        IReadOnlyList<SimulationResult> sortedResults,
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

        var classes = sortedResults
            .Select(r => r.Spec.Class)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var scriptObject = new ScriptObject();
        scriptObject.Import(new
        {
            results = sortedResults,
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
            total_specs = sortedResults.Count,
            broken_apl_count = sortedResults.Count(r => r.LikelyBrokenApl),
            good_count = sortedResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Good),
            moderate_count = sortedResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Moderate),
            poor_count = sortedResults.Count(r => !r.LikelyBrokenApl && r.Severity == DeltaSeverity.Poor),
        });

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var html = await template.RenderAsync(context);
        var outputFile = Path.Combine(outputDir, "index.html");
        await File.WriteAllTextAsync(outputFile, html, ct);

        Console.WriteLine($"  ✓ Written → {outputFile}");
    }

    private async Task GenerateDetailPagesAsync(
        IReadOnlyList<SimulationResult> sortedResults,
        IReadOnlyDictionary<string, SpecDetail> detailBySlug,
        string simcVersion,
        DockerImageInfo dockerImage,
        int iterations,
        CancellationToken ct)
    {
        if (detailBySlug.Count == 0)
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

        var resultBySlug = sortedResults.ToDictionary(r => r.DetailSlug, StringComparer.OrdinalIgnoreCase);

        foreach (var detail in detailBySlug.Values)
        {
            if (!resultBySlug.TryGetValue(detail.Slug, out var result))
                continue;

            var scriptObject = new ScriptObject();
            scriptObject.Import(new
            {
                result,
                detail_json = DetailExtractor.SerializeForEmbed(detail),
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
            var outputFile = Path.Combine(detailsDir, $"{detail.Slug}.html");
            await File.WriteAllTextAsync(outputFile, html, ct);
        }

        Console.WriteLine($"  ✓ Written → {detailsDir}/ ({detailBySlug.Count} pages)");
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
