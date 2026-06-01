using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SbaSimc.Models;

namespace SbaSimc.Services;

/// <summary>
/// Shells out to Docker to invoke the SimulationCraft binary inside the community image.
/// Each call is a short-lived `docker run --rm` that writes a JSON result file to a
/// host-side temp directory mounted into the container at /output.
/// </summary>
public class SimcRunner(SimcConfig config)
{
    /// <summary>
    /// Runs a single SimC simulation and returns the raw JSON output string, or null on failure.
    /// </summary>
    /// <param name="profileName">
    ///     Filename (without extension) of the SimC profile inside the container's profiles directory,
    ///     e.g. "T33_Warrior_Arms".
    /// </param>
    /// <param name="aplSource">
    ///     Optional SimC source override. Null = optimal community APL; "blizzard" = Assisted Highlight
    ///     (no GCD penalty); "one_button" = One Button Rotation (with GCD penalty).
    /// </param>
    /// <param name="hostOutputFile">Absolute path on the host where the JSON result should be written.</param>
    /// <param name="additionalOptions">Extra simc options for this spec (e.g. hero talent selection).</param>
    public async Task<string?> RunAsync(
        string profileName,
        string? aplSource,
        string hostOutputFile,
        string additionalOptions = "",
        CancellationToken ct = default)
    {
        var hostOutputDir = Path.GetDirectoryName(hostOutputFile)!;
        Directory.CreateDirectory(hostOutputDir);

        var containerOutputFile = $"/output/{Path.GetFileName(hostOutputFile)}";
        var profilePath = $"{config.ContainerProfilesPath}/{profileName}.simc";

        var args = BuildDockerArgs(hostOutputDir, profilePath, aplSource, containerOutputFile, additionalOptions);

        Console.WriteLine($"    docker {args[..Math.Min(120, args.Length)]}...");

        var psi = new ProcessStartInfo("docker", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Docker process");

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"    ✗ simc exited {process.ExitCode} for {profileName}: {stderr.Trim()}");
            return null;
        }

        if (!File.Exists(hostOutputFile))
        {
            Console.Error.WriteLine($"    ✗ Output file not found after run: {hostOutputFile}");
            return null;
        }

        return await File.ReadAllTextAsync(hostOutputFile, ct);
    }

    /// <summary>
    /// Resolves the configured Docker tag to its digest (or image ID) for site stamping.
    /// </summary>
    public async Task<DockerImageInfo> GetDockerImageInfoAsync(CancellationToken ct = default)
    {
        var tag = config.DockerImage;

        var psi = new ProcessStartInfo("docker", $"image inspect {tag}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new DockerImageInfo(tag, null, null);

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            return new DockerImageInfo(tag, null, null);

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return new DockerImageInfo(tag, null, null);

            var image = doc.RootElement[0];
            var digest = image.TryGetProperty("RepoDigests", out var digests)
                         && digests.ValueKind == JsonValueKind.Array
                         && digests.GetArrayLength() > 0
                ? digests[0].GetString()
                : null;

            var imageId = image.TryGetProperty("Id", out var idProp)
                ? ShortImageId(idProp.GetString())
                : null;

            return new DockerImageInfo(tag, digest, imageId);
        }
        catch (JsonException)
        {
            return new DockerImageInfo(tag, null, null);
        }
    }

    /// <summary>
    /// Queries the SimC container for its version string, used to stamp the generated site.
    /// </summary>
    public async Task<string> GetSimcVersionAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("docker", $"run --rm {config.DockerImage} --version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process is null) return "unknown";

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output.Trim().Split('\n').FirstOrDefault() ?? "unknown";
    }

    private string BuildDockerArgs(
        string hostOutputDir,
        string containerProfilePath,
        string? aplSource,
        string containerOutputFile,
        string additionalOptions)
    {
        var sb = new StringBuilder();
        sb.Append("run --rm");

        // Mount the host temp directory to /output inside the container so simc can write results there.
        // On Windows, Docker Desktop accepts both "C:\path" and "/c/path" formats — we use the native path.
        sb.Append($" -v \"{hostOutputDir}:/output\"");

        sb.Append($" {config.DockerImage}");
        sb.Append($" {containerProfilePath}");
        sb.Append($" iterations={config.Iterations}");

        // json2 is SimC's structured JSON output format (v2). The path is inside the container.
        sb.Append($" json2={containerOutputFile}");

        // Suppress the default HTML report — we only need the JSON.
        sb.Append(" no-output-file=1");

        if (!string.IsNullOrWhiteSpace(additionalOptions))
            sb.Append($" {additionalOptions}");

        if (aplSource == "blizzard")
        {
            // Selects Blizzard's Assisted Highlight APL (no GCD penalty).
            // use_blizzard_action_list=1 is the correct actor-scoped flag; source=blizzard is an
            // unrelated armory data-import tag and does not change the APL.
            sb.Append(" use_blizzard_action_list=1");
        }
        else if (aplSource == "one_button")
        {
            // One Button Rotation: Blizzard's APL + a 25%-of-GCD timing penalty per cast,
            // modelling the delay inherent in the game's one-button cycling mechanic.
            sb.Append(" use_blizzard_action_list=1 one_button_mode=1");
        }

        return sb.ToString();
    }

    private static string? ShortImageId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        const string prefix = "sha256:";
        var hash = id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? id[prefix.Length..]
            : id;

        return hash.Length <= 12 ? hash : hash[..12];
    }
}
