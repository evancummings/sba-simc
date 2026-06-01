namespace SbaSimc.Models;

public record SimulationResult(
    WowSpec Spec,
    double OptimalDps,
    double AssistedHighlightDps,
    double OneButtonDps,
    bool HasDetailPage = false
)
{
    public string DetailSlug => SpecDetail.SlugFrom(Spec);

    /// <summary>
    /// AH DPS below this fraction of optimal strongly suggests SimC's Blizzard APL is broken
    /// for this profile (e.g. Enhancement Shaman ~3k vs ~112k), not a real in-game result.
    /// </summary>
    public const double BrokenAplThreshold = 0.5;

    /// <summary>
    /// True when Assisted Highlight DPS is far below optimal — likely a SimC assisted_combat bug.
    /// </summary>
    public bool LikelyBrokenApl => OptimalDps > 0
        && AssistedHighlightDps < OptimalDps * BrokenAplThreshold;

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

    public double OneButtonDeltaPercent => OptimalDps > 0
        ? (OneButtonDps - OptimalDps) / OptimalDps * 100.0
        : 0.0;

    public string OneButtonDeltaFormatted => $"{OneButtonDeltaPercent:+0.0;-0.0}%";

    public DeltaSeverity OneButtonSeverity => OneButtonDeltaPercent switch
    {
        >= -5 => DeltaSeverity.Good,
        >= -15 => DeltaSeverity.Moderate,
        _ => DeltaSeverity.Poor
    };
}

public enum DeltaSeverity { Good, Moderate, Poor }
