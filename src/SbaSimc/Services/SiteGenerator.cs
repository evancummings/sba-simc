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
        string simcVersion,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var templatePath = ResolveTemplatePath();
        var templateText = await File.ReadAllTextAsync(templatePath, ct);
        var template = Template.Parse(templateText);

        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Template parse errors: {string.Join("; ", template.Messages)}");

        var sortedResults = results
            .OrderBy(r => r.Spec.Class)
            .ThenBy(r => r.Spec.Spec)
            .ThenBy(r => r.Spec.HeroTalent)
            .ToList();

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
            total_specs = sortedResults.Count,
            good_count = sortedResults.Count(r => r.Severity == DeltaSeverity.Good),
            moderate_count = sortedResults.Count(r => r.Severity == DeltaSeverity.Moderate),
            poor_count = sortedResults.Count(r => r.Severity == DeltaSeverity.Poor),
        });

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var html = await template.RenderAsync(context);
        var outputFile = Path.Combine(outputDir, "index.html");
        await File.WriteAllTextAsync(outputFile, html, ct);

        Console.WriteLine($"  ✓ Written → {outputFile}");
    }

    private static string ResolveTemplatePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "templates", "index.html.sbn"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "templates", "index.html.sbn"),
            Path.Combine(Directory.GetCurrentDirectory(), "templates", "index.html.sbn"),
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"Could not find index.html.sbn template. Tried:\n{string.Join("\n", candidates)}");
    }
}
