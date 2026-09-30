namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2 — constants of the showcase (fictional example companies). Nothing here depends on the
/// date or on the database: the ids, the slug prefix and the phone block are part of the product's contract with nginx
/// (<c>primer-</c> slugs get <c>X-Robots-Tag</c>), with the phone-matching filters and with the operator's runbook.
/// </summary>
public static class ShowcaseCatalog
{
    /// <summary>Slug prefix of every showcase company (§577.3). Reserved: <c>CompanyCreationService</c> refuses it for real
    /// companies, and nginx marks <c>/company/primer-*</c> and <c>/embed/primer-*</c> with <c>noindex</c>.</summary>
    public const string SlugPrefix = "primer-";

    /// <summary>The hidden service tariff of showcase billing accounts (§573.2). Id is a literal so that every machine and
    /// every re-seed refers to the same row. Never assignable to a real account (<c>ShowcaseMixingGuard</c>).</summary>
    public static readonly Guid ShowcasePlanId = Guid.Parse("5a1e0c28-0000-4000-8000-000000000900");

    public const string ShowcasePlanName = "Витрина (служебный)";

    /// <summary>UUIDv5 namespace for <c>ShowcaseIds</c>. Random once, then frozen forever: changing it changes every id.</summary>
    public static readonly Guid IdNamespace = Guid.Parse("c28e6b0a-5f3d-4b7e-9a41-7d2f0e8c1b56");

    /// <summary>First and last phone of the generator's block, canonical form (11 digits, leading 7): <c>+7 (200) 555-00-00</c> …
    /// <c>+7 (200) 555-99-99</c> (§574.4). The code 200 is outside the Russian numbering plan.</summary>
    public const long PhoneBlockFirst = 72005550000L;
    public const long PhoneBlockLast = 72005559999L;

    public const string InstanceKindKey = "instance.kind";
    public const string LastReseedKey = "showcase.last-reseed-utc";

    /// <summary>Advisory-lock key shared by every command that creates or deletes showcase data (and, in pass B, the nightly demo reset).</summary>
    public const string LockKey = "ops:showcase";
    public const string TariffsLockKey = "ops:tariffs";
}
