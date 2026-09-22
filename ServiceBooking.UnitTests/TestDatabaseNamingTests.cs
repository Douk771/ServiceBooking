using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// US-85: защита от сноса чужой/боевой базы тестовым прогоном.
/// См. ARCHITECTURE_CYCLE8.md §69.3.
/// </summary>
public class TestDatabaseNamingTests
{
    [Theory]
    [InlineData("sbtest_a3f19c7b_api")]
    [InlineData("sbtest_00000000_template")]
    [InlineData("sbtest_deadbeef_legal")]
    [InlineData("sbtest_deadbeef_dispatch")]
    public void IsDisposable_returns_true_for_well_formed_names(string databaseName)
    {
        TestDatabaseNaming.IsDisposable(databaseName).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("servicebooking")]
    [InlineData("servicebooking_test")]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("SBTEST_A3F19C7B_API")]                                  // регистр
    [InlineData("sbtest_zzz_api")]                                       // ключ не hex
    [InlineData("sbtest_a3f19c7b_api; DROP DATABASE servicebooking")]    // инъекция
    [InlineData("sbtest_a3f19c7b_")]                                     // пустой слот
    [InlineData("sbtest_a3f19c7bx_api")]                                 // ключ не ровно 8 символов
    public void IsDisposable_returns_false_for_rejected_names(string? databaseName)
    {
        TestDatabaseNaming.IsDisposable(databaseName).Should().BeFalse();
    }

    [Fact]
    public void IsDisposable_returns_false_for_name_with_trailing_newline()
    {
        // Review finding N1: .NET's "$" anchor matches immediately before a trailing '\n', so
        // "sbtest_a3f19c7b_api\n" used to satisfy the old ^...$ pattern even though it isn't a valid
        // Postgres identifier and nothing in this codebase ever produces a name with an embedded
        // newline. \A...\z anchors to the true start/end of the string instead.
        TestDatabaseNaming.IsDisposable("sbtest_a3f19c7b_api\n").Should().BeFalse();
    }

    [Fact]
    public void IsDisposable_returns_false_for_name_longer_than_postgres_limit()
    {
        var tooLong = "sbtest_a3f19c7b_" + new string('a', 60);

        TestDatabaseNaming.IsDisposable(tooLong).Should().BeFalse();
    }

    [Fact]
    public void EnsureDisposable_does_not_throw_for_well_formed_name()
    {
        var act = () => TestDatabaseNaming.EnsureDisposable("sbtest_a3f19c7b_api");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("servicebooking")]
    [InlineData("postgres")]
    [InlineData(null)]
    public void EnsureDisposable_throws_TestSafetyException_for_rejected_name(string? databaseName)
    {
        var act = () => TestDatabaseNaming.EnsureDisposable(databaseName);

        act.Should().Throw<TestSafetyException>()
            .WithMessage("*Отказ*");
    }

    [Fact]
    public void EnsureOwnedByThisRun_throws_for_database_belonging_to_another_run()
    {
        // Review finding N20: a hardcoded "deadbeef" foreign key made this test's own premise depend on
        // the environment — it would fail (for the wrong reason) if SERVICEBOOKING_TEST_RUN_KEY=deadbeef
        // happened to be set. Derive a key that is guaranteed to differ from TestRunKey.Current instead
        // of hoping a literal never collides with it.
        var foreignKey = ForeignKeyDifferentFrom(TestRunKey.Current);
        var foreignDatabase = $"sbtest_{foreignKey}_api";

        foreignDatabase.Should().NotContain(TestRunKey.Current);

        var act = () => TestDatabaseNaming.EnsureOwnedByThisRun(foreignDatabase);

        act.Should().Throw<TestSafetyException>()
            .WithMessage("*другому прогону*");
    }

    [Fact]
    public void EnsureOwnedByThisRun_does_not_throw_for_database_of_current_run()
    {
        var ownDatabase = $"sbtest_{TestRunKey.Current}_api";

        var act = () => TestDatabaseNaming.EnsureOwnedByThisRun(ownDatabase);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureOwnedByThisRun_throws_TestSafetyException_first_when_name_is_not_disposable_at_all()
    {
        var act = () => TestDatabaseNaming.EnsureOwnedByThisRun("servicebooking");

        act.Should().Throw<TestSafetyException>()
            .WithMessage("*не являющуюся одноразовой тестовой*");
    }

    /// <summary>Flips every hex digit of <paramref name="key"/> to its complement within 0-f (15-digit),
    /// guaranteeing the result is 8 hex chars, well-formed, and never equal to <paramref name="key"/>.</summary>
    private static string ForeignKeyDifferentFrom(string key) =>
        new(key.Select(c => "0123456789abcdef"[15 - "0123456789abcdef".IndexOf(c)]).ToArray());
}
