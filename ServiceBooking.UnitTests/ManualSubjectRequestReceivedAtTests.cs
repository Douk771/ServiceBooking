using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Code-review finding (cycle 20) — see <see cref="ManualSubjectRequestReceivedAt"/>'s own doc comment.
/// Only the timezone-independent boundaries are covered here: a scenario built on
/// <see cref="DateTimeKind.Local"/> input would depend on the test machine's own local time zone offset
/// (flaky across environments), so that specific conversion is left to QA's functional coverage instead
/// (see the final report's suggested scenario list) rather than asserted here.
/// </summary>
public class ManualSubjectRequestReceivedAtTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_Null_ReturnsError()
    {
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(null, NowUtc);
        value.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Validate_UtcValueInThePast_Ok()
    {
        var receivedAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(receivedAt, NowUtc);

        error.Should().BeNull();
        value.Should().Be(receivedAt);
    }

    [Fact]
    public void Validate_UtcValueEqualToNow_Ok()
    {
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(NowUtc, NowUtc);
        error.Should().BeNull();
        value.Should().Be(NowUtc);
    }

    [Fact]
    public void Validate_UtcValueInTheFuture_ReturnsError()
    {
        var receivedAt = NowUtc.AddSeconds(1);
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(receivedAt, NowUtc);

        value.Should().BeNull();
        error.Should().Be("Дата поступления не может быть в будущем.");
    }

    [Fact]
    public void Validate_DefaultDateTime_ReturnsError()
    {
        // §410 (US-20-09): a malformed/omitted request body deserializing to default(DateTime) must not
        // sail through as "received on 0001-01-01" — the old code had no floor at all.
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(default, NowUtc);

        value.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Validate_JustBeforeEarliestPlausible_ReturnsError()
    {
        var receivedAt = ManualSubjectRequestReceivedAt.EarliestPlausibleUtc.AddTicks(-1);
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(receivedAt, NowUtc);

        value.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Validate_ExactlyEarliestPlausible_Ok()
    {
        var (value, error) = ManualSubjectRequestReceivedAt.Validate(ManualSubjectRequestReceivedAt.EarliestPlausibleUtc, NowUtc);

        error.Should().BeNull();
        value.Should().Be(ManualSubjectRequestReceivedAt.EarliestPlausibleUtc);
    }

    [Fact]
    public void Validate_UtcKindValue_IsANoOp()
    {
        // The documented 'Z' contract shape (API_CONTRACT_CYCLE20.md §438) deserializes to Kind=Utc —
        // ToUniversalTime() must leave it byte-for-byte unchanged, unlike the old SpecifyKind call which
        // happened to also be a no-op here (the bug only showed up for Kind=Local/Unspecified input).
        var receivedAt = new DateTime(2026, 9, 20, 15, 30, 0, DateTimeKind.Utc);
        var (value, _) = ManualSubjectRequestReceivedAt.Validate(receivedAt, NowUtc);

        value.Should().Be(receivedAt);
        value!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Validate_UnspecifiedKindValue_IsReadAsUtc()
    {
        // An ISO string without offset ("2026-09-20T15:30:00") deserializes to Kind=Unspecified —
        // it must be read as UTC, not shifted by the server's local offset.
        var receivedAt = new DateTime(2026, 9, 20, 15, 30, 0, DateTimeKind.Unspecified);
        var (value, _) = ManualSubjectRequestReceivedAt.Validate(receivedAt, NowUtc);

        value.Should().Be(new DateTime(2026, 9, 20, 15, 30, 0, DateTimeKind.Utc));
        value!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
}
