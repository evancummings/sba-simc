namespace SbaSimc;

public class SimcConfig
{
    public string DockerImage { get; set; } = "simulationcraftorg/simc:latest";

    /// <summary>
    /// Number of iterations per simulation run. Lower = faster, higher = more accurate.
    /// Override via environment variable SIMC__Iterations or appsettings.json.
    /// </summary>
    public int Iterations { get; set; } = 1000;

    /// <summary>
    /// How many simulations to run concurrently. Each sim spawns a Docker container,
    /// so keep this within your CPU/memory budget.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// Path inside the SimC container where default .simc profile files live.
    /// Run `docker run --rm simulationcraftorg/simc:latest find / -name "T3*.simc" 2>/dev/null`
    /// to verify the actual path in the current image.
    /// </summary>
    public string ContainerProfilesPath { get; set; } = "/usr/share/simc/profiles";

    /// <summary>
    /// Path inside the container where assist_combat APL files live (Blizzard's SBA APLs).
    /// These sit alongside the default APLs as siblings in an assist_combat/ subdirectory.
    /// </summary>
    public string ContainerAplPath { get; set; } = "/usr/share/simc/engine/class_modules/apl";
}

public class OutputConfig
{
    public string Directory { get; set; } = "./output";
}
