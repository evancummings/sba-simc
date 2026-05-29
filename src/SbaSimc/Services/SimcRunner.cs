using System.Diagnostics;
using System.Text;

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
    /// <param name="useBlizzardApl">
    ///     When true, disables the default optimal APL and loads the assist_combat APL instead.
    /// </param>
    /// <param name="hostOutputFile">Absolute path on the host where the JSON result should be written.</param>
    /// <param name="additionalOptions">Extra simc options for this spec (e.g. hero talent selection).</param>
    public async Task<string?> RunAsync(
        string profileName,
        bool useBlizzardApl,
        string hostOutputFile,
        string additionalOptions = "",
        CancellationToken ct = default)
    {
        var hostOutputDir = Path.GetDirectoryName(hostOutputFile)!;
        Directory.CreateDirectory(hostOutputDir);

        var containerOutputFile = $"/output/{Path.GetFileName(hostOutputFile)}";
        var profilePath = $"{config.ContainerProfilesPath}/{profileName}.simc";

        var args = BuildDockerArgs(hostOutputDir, profilePath, useBlizzardApl, containerOutputFile, additionalOptions);

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
        bool useBlizzardApl,
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

        if (useBlizzardApl)
        {
            // `source=blizzard` switches the profile to Blizzard's Assisted Combat (Single Button
            // Assistant) APL, which is compiled into the simc binary per-class.
            // Confirmed working: the json2 output shows `profile_source: "blizzard"` when set.
            sb.Append(" source=blizzard");
        }

        return sb.ToString();
    }
}
