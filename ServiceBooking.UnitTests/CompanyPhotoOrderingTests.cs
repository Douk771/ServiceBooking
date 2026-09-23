using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

public class CompanyPhotoOrderingTests
{
    private static CompanyPhoto Photo(Guid id, int position, DateTime createdAtUtc) => new()
    {
        Id = id,
        CompanyId = Guid.NewGuid(),
        Position = position,
        CreatedAtUtc = createdAtUtc,
        Url = "/uploads/companies/x.jpg",
        ThumbnailUrl = "/uploads/companies/x-thumb.jpg",
    };

    // ── ApplyOrder ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyOrder_FullPermutation_SetsPositionsToNewIndexes()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = Photo(Guid.NewGuid(), 1, DateTime.UtcNow);
        var c = Photo(Guid.NewGuid(), 2, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a, b, c };

        CompanyPhotoOrdering.ApplyOrder(current, [c.Id, a.Id, b.Id]);

        c.Position.Should().Be(0);
        a.Position.Should().Be(1);
        b.Position.Should().Be(2);
    }

    [Fact]
    public void ApplyOrder_FirstInList_BecomesCover()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = Photo(Guid.NewGuid(), 1, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a, b };

        CompanyPhotoOrdering.ApplyOrder(current, [b.Id, a.Id]);

        b.Position.Should().Be(0);
    }

    [Fact]
    public void ApplyOrder_MissingAnId_ThrowsInvalidPhotoReorder()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = Photo(Guid.NewGuid(), 1, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a, b };

        var act = () => CompanyPhotoOrdering.ApplyOrder(current, [a.Id]);

        act.Should().Throw<InvalidPhotoReorderException>();
    }

    [Fact]
    public void ApplyOrder_DuplicateId_ThrowsInvalidPhotoReorder()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = Photo(Guid.NewGuid(), 1, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a, b };

        var act = () => CompanyPhotoOrdering.ApplyOrder(current, [a.Id, a.Id]);

        act.Should().Throw<InvalidPhotoReorderException>();
    }

    [Fact]
    public void ApplyOrder_UnknownId_ThrowsInvalidPhotoReorder()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = Photo(Guid.NewGuid(), 1, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a, b };

        var act = () => CompanyPhotoOrdering.ApplyOrder(current, [a.Id, Guid.NewGuid()]);

        act.Should().Throw<InvalidPhotoReorderException>();
    }

    [Fact]
    public void ApplyOrder_ExtraId_ThrowsInvalidPhotoReorder()
    {
        var a = Photo(Guid.NewGuid(), 0, DateTime.UtcNow);
        var current = new List<CompanyPhoto> { a };

        var act = () => CompanyPhotoOrdering.ApplyOrder(current, [a.Id, Guid.NewGuid()]);

        act.Should().Throw<InvalidPhotoReorderException>();
    }

    // ── Compact ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compact_AfterRemovingCover_NextByPositionBecomesTheNewCover()
    {
        var now = DateTime.UtcNow;
        // Simulates deleting the old position-0 photo: the remaining rows still carry positions 1 and 2.
        var second = Photo(Guid.NewGuid(), 1, now);
        var third = Photo(Guid.NewGuid(), 2, now);

        CompanyPhotoOrdering.Compact([second, third]);

        second.Position.Should().Be(0);
        third.Position.Should().Be(1);
    }

    [Fact]
    public void Compact_ProducesContiguousZeroBasedRange()
    {
        var now = DateTime.UtcNow;
        var a = Photo(Guid.NewGuid(), 5, now);
        var b = Photo(Guid.NewGuid(), 7, now);
        var c = Photo(Guid.NewGuid(), 9, now);

        CompanyPhotoOrdering.Compact([a, b, c]);

        new[] { a.Position, b.Position, c.Position }.Should().BeEquivalentTo([0, 1, 2]);
    }

    [Fact]
    public void Compact_TiedPositions_BreaksTieByCreatedAtThenId()
    {
        var earlier = DateTime.UtcNow;
        var later = earlier.AddMinutes(1);
        var idLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idHigh = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // Two rows that "somehow" ended up sharing a position (the whole reason §102.2 keeps the DB
        // index non-unique) — CreatedAtUtc breaks the tie first.
        var newerSamePosition = Photo(idHigh, 0, later);
        var olderSamePosition = Photo(idLow, 0, earlier);

        CompanyPhotoOrdering.Compact([newerSamePosition, olderSamePosition]);

        olderSamePosition.Position.Should().Be(0);
        newerSamePosition.Position.Should().Be(1);
    }

    [Fact]
    public void Compact_EmptyList_DoesNothing()
    {
        var act = () => CompanyPhotoOrdering.Compact([]);

        act.Should().NotThrow();
    }

    // ── SelectCovers ─────────────────────────────────────────────────────────

    private static CompanyPhoto PhotoFor(Guid companyId, Guid id, int position, DateTime createdAtUtc) => new()
    {
        Id = id,
        CompanyId = companyId,
        Position = position,
        CreatedAtUtc = createdAtUtc,
        Url = $"/uploads/companies/{id}.jpg",
        ThumbnailUrl = $"/uploads/companies/{id}-thumb.jpg",
    };

    [Fact]
    public void SelectCovers_OneRowPerCompany_ReturnsThatRow()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var a = PhotoFor(companyA, Guid.NewGuid(), 0, DateTime.UtcNow);
        var b = PhotoFor(companyB, Guid.NewGuid(), 0, DateTime.UtcNow);

        var covers = CompanyPhotoOrdering.SelectCovers([a, b]);

        covers.Should().HaveCount(2);
        covers[companyA].Should().BeSameAs(a);
        covers[companyB].Should().BeSameAs(b);
    }

    [Fact]
    public void SelectCovers_DuplicatePositionZeroForSameCompany_DoesNotThrow_PicksOldestCreatedAt()
    {
        // §102.2: (CompanyId, Position) is deliberately non-unique — two rows can legitimately share
        // Position == 0 for the same company. A naive ToDictionary would throw ArgumentException here.
        var companyId = Guid.NewGuid();
        var earlier = DateTime.UtcNow;
        var later = earlier.AddMinutes(5);
        var older = PhotoFor(companyId, Guid.NewGuid(), 0, earlier);
        var newer = PhotoFor(companyId, Guid.NewGuid(), 0, later);

        var act = () => CompanyPhotoOrdering.SelectCovers([newer, older]);

        act.Should().NotThrow();
        var covers = act();
        covers.Should().HaveCount(1);
        covers[companyId].Should().BeSameAs(older);
    }

    [Fact]
    public void SelectCovers_DuplicatePositionZeroTiedOnCreatedAt_BreaksTieById()
    {
        var companyId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var idLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idHigh = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var low = PhotoFor(companyId, idLow, 0, now);
        var high = PhotoFor(companyId, idHigh, 0, now);

        var covers = CompanyPhotoOrdering.SelectCovers([high, low]);

        covers[companyId].Should().BeSameAs(low);
    }

    [Fact]
    public void SelectCovers_EmptyInput_ReturnsEmptyDictionary()
    {
        var covers = CompanyPhotoOrdering.SelectCovers([]);

        covers.Should().BeEmpty();
    }
}
