namespace SbaSimc.Models;

public record SimulationResult(
    WowSpec Spec,
    double OptimalDps,
    double AssistedHighlightDps,
    double OneButtonDps
)
{
    /// <summary>
    /// How much DPS the Assisted Highlight APL loses relative to the optimal APL.
    /// Negative means underperformance (expected). Positive would mean it outperforms.
    /// </summary>
    public double DeltaPercent => OptimalDps > 0
        ? (AssistedHighlightDps - OptimalDps) / OptimalDps * 100.0
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
