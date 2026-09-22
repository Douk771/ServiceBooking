namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// The one implementation of "give me a phone number no other test-data row will ever collide with",
/// shared by <see cref="ApiTestBase"/> and <see cref="NotificationTestBase"/> (previously two copies of
/// the same method — ARCHITECTURE_CYCLE8.md §71.2). Guid-derived, so collision-free both within a single
/// run and across runs (different databases per run make cross-run collisions moot anyway). Always in the
/// <c>+79…</c> range, which never overlaps the <c>+7000000000x</c> range <see cref="TestHostSettings"/>
/// reserves for per-factory SuperAdmin accounts (§71.1).
/// </summary>
public static class TestPhones
{
    public static string Unique()
    {
        // 11 digits after "+", collision-free within a run (Guid-derived), fits a plausible RU format.
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(10).ToArray();
        var suffix = new string(digits).PadRight(10, '0');
        return $"+79{suffix[..9]}";
    }
}
