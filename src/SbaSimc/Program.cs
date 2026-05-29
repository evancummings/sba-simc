using Microsoft.Extensions.Configuration;
using SbaSimc;
using SbaSimc.Models;
using SbaSimc.Services;
using System.Collections.Concurrent;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    // Environment variable overrides — double-underscore is the section separator.
    // Examples:
    //   SIMC__Iterations=5000
    //   SIMC__MaxParallelism=8
    //   SIMC__DockerImage=simulationcraftorg/simc:nightly
    .AddEnvironmentVariables()
    .Build();

var simcConfig  = configuration.GetSection("SimC").Get<SimcConfig>()
    ?? throw new InvalidOperationException("SimC config section is missing or invalid.");
var outputConfig = configuration.GetSection("Output").Get<OutputConfig>()
    ?? throw new InvalidOperationException("Output config section is missing or invalid.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("=== SBA SimC — Blizzard APL vs Optimal APL Comparison ===");
Console.WriteLine($"  Docker image : {simcConfig.DockerImage}");
Console.WriteLine($"  Iterations   : {simcConfig.Iterations:N0}");
Console.WriteLine($"  Parallelism  : {simcConfig.MaxParallelism}");
Console.WriteLine($"  Output dir   : {outputConfig.Directory}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Load specs
// ---------------------------------------------------------------------------
var specsPath = Path.Combine(AppContext.BaseDirectory, "specs.json");
Console.WriteLine($"Loading specs from {specsPath} ...");
var specs = await SpecLoader.LoadAsync(specsPath, cts.Token);
Console.WriteLine($"  {specs.Count} spec/hero-talent combinations loaded.");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Run simulations
// ---------------------------------------------------------------------------
var tempDir = Path.Combine(Path.GetTempPath(), "sba-simc");
Directory.CreateDirectory(tempDir);

var runner  = new SimcRunner(simcConfig);
var results = new ConcurrentBag<SimulationResult>();

Console.WriteLine("Running simulations...");

await Parallel.ForEachAsync(
    specs,
    new ParallelOptions { MaxDegreeOfParallelism = simcConfig.MaxParallelism, CancellationToken = cts.Token },
    async (spec, ct) =>
    {
        var label = $"{spec.Class} {spec.Spec} [{spec.HeroTalent}]";
        Console.WriteLine($"  ▶ {label}");

        var safeName = $"{spec.SimcProfileName}_{spec.HeroTalent}"
            .Replace("'", "")
            .Replace(" ", "_");

        var optOutputFile  = Path.Combine(tempDir, $"{safeName}_optimal.json");
        var blizOutputFile = Path.Combine(tempDir, $"{safeName}_blizzard.json");

        string? optJson  = await runner.RunAsync(spec.SimcProfileName, useBlizzardApl: false, optOutputFile,  spec.AdditionalSimcOptions, ct);
        string? blizJson = await runner.RunAsync(spec.SimcProfileName, useBlizzardApl: true,  blizOutputFile, spec.AdditionalSimcOptions, ct);

        if (optJson is null || blizJson is null)
        {
            Console.Error.WriteLine($"  ✗ Skipping {label} — simulation run failed.");
            return;
        }

        var optDps  = ResultParser.ExtractDps(optJson);
        var blizDps = ResultParser.ExtractDps(blizJson);

        if (optDps is null || blizDps is null)
        {
            Console.Error.WriteLine($"  ✗ Skipping {label} — could not parse DPS from output.");
            return;
        }

        var result = new SimulationResult(spec, optDps.Value, blizDps.Value);
        results.Add(result);

        Console.WriteLine($"  ✓ {label}");
        Console.WriteLine($"      Optimal: {optDps.Value:N0}  |  Blizzard: {blizDps.Value:N0}  |  Δ {result.DeltaFormatted}");
    });

Console.WriteLine();
Console.WriteLine($"Completed {results.Count}/{specs.Count} simulations.");

if (results.IsEmpty)
{
    Console.Error.WriteLine("No results produced — site generation skipped.");
    return 1;
}

// ---------------------------------------------------------------------------
// Extract SimC version from the first successful result file
// ---------------------------------------------------------------------------
var firstResultFile = Directory.EnumerateFiles(tempDir, "*_optimal.json").FirstOrDefault();
var simcVersion = "unknown";
if (firstResultFile is not null)
{
    var json = await File.ReadAllTextAsync(firstResultFile);
    simcVersion = ResultParser.ExtractVersion(json) ?? "unknown";
}

// ---------------------------------------------------------------------------
// Generate site
// ---------------------------------------------------------------------------
Console.WriteLine("Generating static site...");
var generator = new SiteGenerator(outputConfig.Directory);
await generator.GenerateAsync(results, simcVersion, cts.Token);

Console.WriteLine();
Console.WriteLine("Done.");
return 0;
