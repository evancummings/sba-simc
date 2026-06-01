using Microsoft.Extensions.Configuration;
using SbaSimc;
using SbaSimc.Models;
using SbaSimc.Services;
using System.Collections.Concurrent;

if (args.Length > 0 && args[0] == "test-detail")
    return await RunDetailPageTest(args.Skip(1).ToArray());

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

var runner  = new SimcRunner(simcConfig);
var dockerImage = await runner.GetDockerImageInfoAsync(cts.Token);
var results = new ConcurrentBag<SimulationResult>();
var details = new ConcurrentBag<SpecDetail>();

Console.WriteLine("=== SBA SimC — Optimal vs Assisted Highlight vs One Button Rotation ===");
Console.WriteLine($"  Docker image : {dockerImage.DisplayLabel}");
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

        var optOutputFile = Path.Combine(tempDir, $"{safeName}_optimal.json");
        var ahOutputFile  = Path.Combine(tempDir, $"{safeName}_assisted_highlight.json");
        var obOutputFile  = Path.Combine(tempDir, $"{safeName}_one_button.json");

        string? optJson = await runner.RunAsync(spec.SimcProfileName, aplSource: null,          optOutputFile, spec.AdditionalSimcOptions, ct);
        string? ahJson  = await runner.RunAsync(spec.SimcProfileName, aplSource: "blizzard",    ahOutputFile,  spec.AdditionalSimcOptions, ct);
        string? obJson  = await runner.RunAsync(spec.SimcProfileName, aplSource: "one_button",  obOutputFile,  spec.AdditionalSimcOptions, ct);

        if (optJson is null || ahJson is null || obJson is null)
        {
            Console.Error.WriteLine($"  ✗ Skipping {label} — simulation run failed.");
            return;
        }

        var optDps = ResultParser.ExtractDps(optJson);
        var ahDps  = ResultParser.ExtractDps(ahJson);
        var obDps  = ResultParser.ExtractDps(obJson);

        if (optDps is null || ahDps is null || obDps is null)
        {
            Console.Error.WriteLine($"  ✗ Skipping {label} — could not parse DPS from output.");
            return;
        }

        var result = new SimulationResult(spec, optDps.Value, ahDps.Value, obDps.Value);
        results.Add(result);

        var detail = DetailExtractor.Extract(spec, optJson, ahJson, obJson, simcConfig.Iterations);
        if (detail is not null)
            details.Add(detail);
        else
            Console.Error.WriteLine($"  ⚠ {label} — detail extraction failed (index row kept).");

        Console.WriteLine($"  ✓ {label}");
        Console.WriteLine($"      Optimal: {optDps.Value:N0}  |  AH: {ahDps.Value:N0}  |  OB: {obDps.Value:N0}  |  Δ {result.DeltaFormatted}");
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
await generator.GenerateAsync(results, details, simcVersion, dockerImage, simcConfig.Iterations, cts.Token);

Console.WriteLine();
Console.WriteLine("Done.");
return 0;

static async Task<int> RunDetailPageTest(string[] args)
{
    var optPath = args.ElementAtOrDefault(0) ?? @"C:\Temp\simc-test\probe.json";
    var ahPath  = args.ElementAtOrDefault(1) ?? @"C:\Temp\simc-test\probe_ah.json";
    var obPath  = args.ElementAtOrDefault(2) ?? @"C:\Temp\simc-test\probe_ob.json";

    var spec = new WowSpec("Warrior", "Arms", "Slayer", "MID1_Warrior_Arms");
    var optJson = await File.ReadAllTextAsync(optPath);
    var ahJson  = await File.ReadAllTextAsync(ahPath);
    var obJson  = await File.ReadAllTextAsync(obPath);

    var detail = DetailExtractor.Extract(spec, optJson, ahJson, obJson, iterations: 5);
    if (detail is null)
    {
        Console.Error.WriteLine("Detail extraction failed.");
        return 1;
    }

    var result = new SimulationResult(spec, detail.Simc.Dps, detail.AssistedHighlight.Dps, detail.OneButton.Dps, HasDetailPage: true);
    var dockerImage = new DockerImageInfo("simulationcraftorg/simc:latest", "sha256:test", "test");
    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
    var generator = new SiteGenerator(outputDir);
    await generator.GenerateAsync(
        [result],
        [detail],
        ResultParser.ExtractVersion(optJson) ?? "test",
        dockerImage,
        5);

    Console.WriteLine($"Detail page → {Path.Combine(outputDir, "details", detail.Slug + ".html")}");
    Console.WriteLine($"Abilities extracted: {detail.Abilities.Count}");
    return 0;
}
