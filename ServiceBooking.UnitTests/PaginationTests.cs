using FluentAssertions;
using ServiceBooking.API.DTOs.Common;

namespace ServiceBooking.UnitTests;

public class PaginationTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Normalize_Page_ClampsBelowOneToOne(int? page, int expected) =>
        Pagination.Normalize(page, null).Page.Should().Be(expected);

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Normalize_PageSize_DefaultsAndKeepsWithinBounds(int? pageSize, int expected) =>
        Pagination.Normalize(null, pageSize).PageSize.Should().Be(expected);

    [Fact]
    public void Normalize_PageSizeAboveMax_IsClampedNotRejected()
    {
        // US-49 p.6: silently clamped to 100, not a 400 — an oversized pageSize is not a client error.
        var (_, pageSize) = Pagination.Normalize(1, 1000);
        pageSize.Should().Be(100);
    }

    // Blocker B3: page=int.MaxValue used to flow straight through unclamped, so every call site's
    // (page - 1) * pageSize overflowed a 32-bit int — a negative OFFSET, 500 on a public anonymous
    // endpoint (GET /api/companies/{id}/reviews?page=2147483647). These pin the fix at the boundary.
    [Fact]
    public void Normalize_MaxIntPage_IsClampedToKeepOffsetComputationOverflowSafe()
    {
        var (page, pageSize) = Pagination.Normalize(int.MaxValue, null);

        pageSize.Should().Be(Pagination.DefaultPageSize);
        page.Should().BeLessOrEqualTo(int.MaxValue / pageSize);

        // The exact computation every call site performs — must not overflow / go negative.
        var offset = (page - 1) * pageSize;
        offset.Should().BeGreaterOrEqualTo(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public void Normalize_MaxIntPage_StaysOverflowSafeForEveryPageSize(int requestedPageSize)
    {
        var (page, pageSize) = Pagination.Normalize(int.MaxValue, requestedPageSize);

        var offset = (long)(page - 1) * pageSize;
        offset.Should().BeInRange(0, int.MaxValue);
        // And the int-typed computation every call site actually does must agree with the long one.
        ((int)offset).Should().Be((page - 1) * pageSize);
    }

    [Fact]
    public void Normalize_PageOne_IsUnaffectedByOverflowClamp()
    {
        var (page, _) = Pagination.Normalize(1, null);
        page.Should().Be(1);
    }

    [Fact]
    public void Create_HasNext_TrueWhenMoreRowsRemain()
    {
        var result = Pagination.Create(new[] { 1, 2, 3 }, page: 1, pageSize: 20, total: 137);
        result.HasNext.Should().BeTrue();
    }

    [Fact]
    public void Create_HasNext_FalseOnTheLastPage()
    {
        var result = Pagination.Create(new[] { 1 }, page: 7, pageSize: 20, total: 137);
        result.HasNext.Should().BeFalse();
    }

    [Fact]
    public void Create_HasNext_FalseWhenExactlyAtTheBoundary()
    {
        // page * pageSize == total means this page ends exactly on the last row — nothing left after it.
        var result = Pagination.Create(Array.Empty<int>(), page: 5, pageSize: 20, total: 100);
        result.HasNext.Should().BeFalse();
    }

    [Fact]
    public void Create_PropagatesPageAndPageSizeAndTotalUnchanged()
    {
        var result = Pagination.Create(new[] { "a" }, page: 3, pageSize: 10, total: 42);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.Total.Should().Be(42);
        result.Items.Should().Equal("a");
    }

    // Cycle-07 backend report, item 1: contracts/cycle7/openapi.yaml's PagedAdminBillingAccounts /
    // PagedAdminSubscriptionRequests are `additionalProperties: false` with
    // required [items, page, pageSize, totalCount] — no `total`, no `hasNext`. CreateContract is the
    // envelope those two cycle-5 endpoints use instead of Create/PagedResult<T>.
    [Fact]
    public void CreateContract_PropagatesPageAndPageSizeAndTotalCountUnchanged()
    {
        var result = Pagination.CreateContract(new[] { "a" }, page: 3, pageSize: 10, totalCount: 42);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(42);
        result.Items.Should().Equal("a");
    }

    [Fact]
    public void CreateContract_HasNoHasNextProperty_TheContractOnlyDeclaresTotalCount() =>
        // ContractPagedResult<T> intentionally does not declare HasNext at all — a compile-time
        // guarantee, not just a runtime assertion, that the two billing-admin list endpoints never
        // regrow the extra field schemathesis flagged as undocumented.
        typeof(ContractPagedResult<string>).GetProperty("HasNext").Should().BeNull();

    // Cycle-07 QA finding #2: GET /api/admin/billing-accounts?search=... 500ed on an embedded NUL byte
    // (Postgres' `text` type rejects it outright) — SanitizeSearch is the one place every `?search=`
    // call site strips it, and any other control character, before the value ever reaches SQL.
    [Theory]
    [InlineData("Иванов\0", "Иванов")]
    [InlineData("\0\0\0", null)]
    [InlineData(" bell-and-escape", " bell-and-escape")]
    [InlineData("no control chars", "no control chars")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void SanitizeSearch_StripsControlCharactersOnly(string? input, string? expected) =>
        Pagination.SanitizeSearch(input).Should().Be(expected);

    [Fact]
    public void SanitizeSearch_KeepsNonLatinTextUntouched() =>
        // Only control characters are stripped — a Cyrillic company name search must not be mangled.
        Pagination.SanitizeSearch("Компания «Ромашка»").Should().Be("Компания «Ромашка»");

    // Cycle-09 backend contract re-check: GET /companies/public bound page/pageSize as `int?`, so a
    // malformed value (e.g. pageSize=false) failed [ApiController] model binding and short-circuited to
    // an automatic 400 — contradicting contracts/cycle9/openapi.yaml's explicit "клампится к [1,100],
    // а не отвергается 400-м" for pageSize. Binding as `string?` and routing through ParseNullableInt
    // first keeps Normalize's existing clamp/default behavior for malformed input too.
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("false", null)]
    [InlineData("not-a-number", null)]
    [InlineData("20", 20)]
    [InlineData("0", 0)]
    [InlineData("-5", -5)]
    [InlineData(" 7 ", 7)] // int.TryParse tolerates surrounding whitespace by default (NumberStyles.Integer).
    public void ParseNullableInt_FallsBackToNullInsteadOfThrowingOnMalformedInput(string? input, int? expected) =>
        Pagination.ParseNullableInt(input).Should().Be(expected);
}
