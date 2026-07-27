namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Attaches a stable, human-referenceable ID to a test method (e.g. "BK-003") for use in
/// TEST_CATALOG.md. The ID is independent of the method's position in the file, so refactoring
/// a test class (reordering, splitting, renaming methods) never invalidates the catalog's
/// cross-references — unlike a file:line link. To find a test from its ID, grep the id string
/// (it appears nowhere else): `grep -rn "BK-003" ServiceBooking.Tests/`.
///
/// Purely documentation metadata — does not affect how xUnit discovers or runs the test.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestCaseAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
