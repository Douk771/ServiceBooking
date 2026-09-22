namespace ServiceBooking.TestKit;

/// <summary>
/// The one source of collision-free values inside a parallelizable test class
/// (ARCHITECTURE_CYCLE8_PHASE2.md §94, Q12). Lives on <c>TestDatabaseFixture</c> — one instance
/// per test class, keyed by that class' database slot ("c07").
///
/// Uniqueness here is by construction, not by convention: two different classes never share a database
/// (§91), so no value this type produces can ever collide with another class' data even if both classes
/// run at the same wall-clock instant. The class slot is folded into every value anyway, purely for
/// diagnosability — ARCHITECTURE_CYCLE8_PHASE2.md §94 notes that a Guid-derived phone number in a
/// database or log line does not say which test created it, and that cost real diagnosis time in a past
/// cycle's failures.
///
/// T9 review (M3-adjacent move): lives in ServiceBooking.TestKit, not ServiceBooking.Tests.Infrastructure
/// where it was originally written, so ServiceBooking.UnitTests (which references TestKit but not
/// ServiceBooking.Tests — the two test projects don't reference each other) can cover
/// <see cref="Phone"/>'s exact output shape directly, instead of that arithmetic only ever being exercised
/// indirectly by a full functional run.
/// </summary>
public sealed class TestData(string classSlot)
{
    private int _n;

    private string Next() => $"{classSlot}{Interlocked.Increment(ref _n):D6}";

    /// <summary>A unique, valid-looking RU phone number ("+79XXXXXXXXX", 12 characters total — the same
    /// shape <c>TestPhones.Unique</c> produces) for this class.</summary>
    public string Phone()
    {
        var digits = new string(Next().Where(char.IsDigit).ToArray());
        digits = digits.Length >= 9 ? digits[^9..] : digits.PadLeft(9, '0');
        return $"+79{digits}";
    }

    public string Email(string prefix = "u") => $"{prefix}{Next()}@test.local";

    /// <summary>A short, lowercase, URL-safe slug — company slugs and similar unique-index-backed codes.</summary>
    public string Slug(string prefix = "company-") => $"{prefix}{Next()}".ToLowerInvariant();

    public string Name(string prefix) => $"{prefix}{Next()}";

    /// <summary>A per-class, per-purpose scratch directory under this run's temp root
    /// (<c>&lt;tmp&gt;/sb-test/&lt;runkey&gt;/&lt;classSlot&gt;/&lt;purpose&gt;</c>), created on first use.</summary>
    public string Dir(string purpose)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sb-test", TestRunKey.Current, classSlot, purpose);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
