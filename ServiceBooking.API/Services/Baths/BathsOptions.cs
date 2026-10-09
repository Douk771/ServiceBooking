namespace ServiceBooking.API.Services.Baths;

/// <summary>ARCHITECTURE_CYCLE42.md §42.5 — configuration section <c>Baths</c> (the numbers of the «Бани» line that are not shared with «Дома»).</summary>
public sealed class BathsOptions
{
    public const string SectionName = "Baths";

    public int TrialDays { get; set; } = 14;

    /// <summary>ARCHITECTURE_CYCLE42.md §42.10.2: lifetime of the cached catalog «base» (published resources + company gates), seconds.</summary>
    public int CatalogCacheSeconds { get; set; } = 30;
}
