namespace SbaSimc.Models;

public record AbilityStat(
    string Name,
    string SpellName,
    double PortionPercent,
    double ExecutesPerFight,
    double? IntervalSec,
    double? CritPercent,
    double AvgHit,
    double TotalDamageMean
)
{
    public bool IsSkipped => ExecutesPerFight <= 0 || PortionPercent < 0.1;
}

public record SampleAction(
    double TimeSec,
    string Name,
    string SpellName,
    string Target
);

public record SimModeDetail(
    double Dps,
    double FightLengthSec,
    string? ProfileSource,
    IReadOnlyList<double> Timeline,
    IReadOnlyList<SampleAction> SampleActions,
    IReadOnlyDictionary<string, AbilityStat> AbilitiesByKey
);

public record AbilityRow(
    string Key,
    string DisplayName,
    AbilityStat? Simc,
    AbilityStat? AssistedHighlight,
    AbilityStat? OneButton
)
{
    public bool SkippedByAh => Simc is not null && !Simc.IsSkipped
        && (AssistedHighlight is null || AssistedHighlight.IsSkipped);

    public bool SkippedByOb => Simc is not null && !Simc.IsSkipped
        && (OneButton is null || OneButton.IsSkipped);

    public double SortPortion => Simc?.PortionPercent ?? AssistedHighlight?.PortionPercent ?? OneButton?.PortionPercent ?? 0;
}

public record SpecDetail(
    string Slug,
    string Class,
    string Spec,
    string HeroTalent,
    string SimcProfileName,
    string PlayerName,
    string? Talents,
    SimModeDetail Simc,
    SimModeDetail AssistedHighlight,
    SimModeDetail OneButton,
    IReadOnlyList<AbilityRow> Abilities,
    string FightStyle,
    int Iterations
)
{
    public static string SlugFrom(WowSpec spec) =>
        $"{spec.SimcProfileName}_{spec.HeroTalent}"
            .Replace("'", "")
            .Replace(" ", "_");
}
