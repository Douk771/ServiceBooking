namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.4 — the three demo roles of <see cref="ShowcaseProfile.Demo"/> and how to find them again. The accounts are ordinary rows of the
/// generated graph with stable UUIDv5 ids (<see cref="ShowcaseIds"/>), so a token issued BEFORE the nightly reset still names an existing user AFTER it
/// (same id, same security stamp). The wire names (<c>owner</c>, <c>master</c>, <c>client</c>) are the contract of <c>POST /api/demo/login</c>.
/// </summary>
public static class ShowcaseDemoRoles
{
    public const string Owner = "owner";
    public const string Master = "master";
    public const string Client = "client";

    /// <summary>The flagship salon: the first company of the dataset, six masters, open for booking.</summary>
    public const string FlagshipCompanyKey = "lavanda";

    // Keys inside the generated ids: «owner key» of the spec, «company:m<i>» of the first master, and one extra client made only for the demo profile.
    public const string OwnerUserKey = "lavanda-owner";
    public const string MasterUserKey = "lavanda:m0";
    public const string ClientUserKey = "demo-client";

    /// <summary>The companies in which the demo client has visits (the flagship and two more open ones), so "Мои визиты" shows several companies.</summary>
    public static readonly IReadOnlyList<string> ClientCompanyKeys = [FlagshipCompanyKey, "zhemchug", "vzglyad"];

    public static readonly IReadOnlyList<(string Role, string Label)> All =
    [
        (Owner, "Войти как владелец салона"),
        (Master, "Войти как мастер"),
        (Client, "Войти как клиент"),
    ];

    public static bool IsKnown(string? role) => role is Owner or Master or Client;

    private static readonly Lazy<IReadOnlySet<string>> DemoUserIds = new(() =>
        All.Select(r => UserIdOf(r.Role)!).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="userId"/> is one of the three demo accounts (decided by the user, not by anything the token claims).</summary>
    public static bool IsDemoUserId(string? userId) => userId is not null && DemoUserIds.Value.Contains(userId);

    /// <summary>The id of the demo account of a role; null for an unknown role.</summary>
    public static string? UserIdOf(string? role) => role switch
    {
        Owner => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", OwnerUserKey).ToString(),
        Master => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", MasterUserKey).ToString(),
        Client => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", ClientUserKey).ToString(),
        _ => null,
    };
}
