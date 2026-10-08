namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2-§37.13 — configuration section <c>Stays</c>. Every number the architecture calls "config" lives here.</summary>
public sealed class StaysOptions
{
    public const string SectionName = "Stays";

    public CatalogCityOptions CatalogCity { get; set; } = new();
    public int MaxStaffPerCompany { get; set; } = 30;
    public int TrialDays { get; set; } = 14;
    public int MaxPhotosPerHouse { get; set; } = 15;
    public int CatalogCacheSeconds { get; set; } = 30;
    public int CatalogPageSize { get; set; } = 20;
    public PhoneLimitsOptions PhoneLimits { get; set; } = new();
    public PaymentProofOptions PaymentProofs { get; set; } = new();
    public int PushSubscriptionsPerBooking { get; set; } = 5;
    public int GuestMessageTtlMinutes { get; set; } = 120;

    /// <summary>Cancellation templates (ЮР-1). Absent = <see cref="StayCancellationRule.Defaults"/>; validated at startup (R37-11).</summary>
    public Dictionary<string, CancellationRuleOptions>? CancellationPolicies { get; set; }

    public sealed class CatalogCityOptions
    {
        public string Name { get; set; } = "Шерегеш";
        public string Region { get; set; } = "Кемеровская область";
    }

    public sealed class PhoneLimitsOptions
    {
        public int MaxHeldPerPhone { get; set; } = 2;
        public int MaxHeldPerPhonePerCompany { get; set; } = 1;
        public int MaxCreatedPerPhonePerDay { get; set; } = 10;
    }

    public sealed class PaymentProofOptions
    {
        public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;
        public int MaxPerBooking { get; set; } = 3;
        public int ViewedEventMinutes { get; set; } = 10;
    }

    public sealed class CancellationRuleOptions
    {
        public string Boundary { get; set; } = "CheckInDayStart";
        public int MaxDeductionNights { get; set; } = 1;
    }

    /// <summary>The rule set in force: configuration over the defaults. Unknown names/boundaries are reported by <see cref="ConfigurationErrors"/>.</summary>
    public IReadOnlyDictionary<Core.Enums.StayCancellationPolicy, StayCancellationRule> EffectiveCancellationRules()
    {
        var rules = new Dictionary<Core.Enums.StayCancellationPolicy, StayCancellationRule>(StayCancellationRule.Defaults);
        if (CancellationPolicies is null) return rules;
        foreach (var (name, o) in CancellationPolicies)
        {
            if (Enum.TryParse<Core.Enums.StayCancellationPolicy>(name, true, out var policy) && Enum.TryParse<CancellationBoundary>(o.Boundary, true, out var boundary))
                rules[policy] = new StayCancellationRule(boundary, o.MaxDeductionNights);
        }
        return rules;
    }

    public IReadOnlyList<string> ConfigurationErrors()
    {
        var errors = new List<string>();
        foreach (var (name, o) in CancellationPolicies ?? [])
        {
            if (!Enum.TryParse<Core.Enums.StayCancellationPolicy>(name, true, out _)) errors.Add($"Stays:CancellationPolicies:{name} is not a known template.");
            if (!Enum.TryParse<CancellationBoundary>(o.Boundary, true, out _)) errors.Add($"Stays:CancellationPolicies:{name}:Boundary '{o.Boundary}' is unknown.");
        }
        errors.AddRange(StayCancellationRule.Validate(EffectiveCancellationRules()));
        return errors;
    }
}
