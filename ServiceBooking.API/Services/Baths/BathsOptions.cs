namespace ServiceBooking.API.Services.Baths;

/// <summary>ARCHITECTURE_CYCLE42.md §42.5 — configuration section <c>Baths</c> (the numbers of the «Бани» line that are not shared with «Дома»).</summary>
public sealed class BathsOptions
{
    public const string SectionName = "Baths";

    public int TrialDays { get; set; } = 14;
}
