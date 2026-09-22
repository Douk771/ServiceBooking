namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// The three database owners a test run creates, per ARCHITECTURE_CYCLE8.md §66.2. A slot is a data
/// owner, not an xUnit collection — <c>LegalDocumentsTestFactory</c> lives in the "Api" xUnit collection
/// (for sequencing only) but owns the <see cref="Legal"/> slot's database.
/// </summary>
public static class TestSlot
{
    public const string Api = "api";
    public const string Legal = "legal";
    public const string Dispatch = "dispatch";

    public static readonly string[] All = [Api, Legal, Dispatch];
}
