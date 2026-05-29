namespace SbaSimc.Models;

public record SimulationResult(
    WowSpec Spec,
    double OptimalDps,
    double BlizzardDps
)
{
    /// <summary>
    /// How much DPS Blizzard's APL loses relative to the optimal APL.
    /// Negative means Blizzard APL underperforms (expected). Positive would mean it somehow outperforms.
    /// </summary>
    public double DeltaPercent => OptimalDps > 0
        ? (BlizzardDps - OptimalDps) / OptimalDps * 100.0
        : 0.0;

    public string DeltaFormatted => $"{DeltaPercent:+0.0;-0.0}%";

    /// <summary>Severity bucket used for HTML colour-coding.</summary>
    public DeltaSeverity Severity => DeltaPercent switch
    {
        >= -5 => DeltaSeverity.Good,
        >= -15 => DeltaSeverity.Moderate,
        _ => DeltaSeverity.Poor
    };
}

public enum DeltaSeverity { Good, Moderate, Poor }
