using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>US-67 (ARCHITECTURE_CYCLE6.md §47.1): pure validation/aggregation rules for a multi-service
/// visit — no DB, so every rule from the spec's Q3 answer (1..5 services, no duplicates, serviceId ==
/// serviceIds[0], duration/price = sum) is exercised directly here.</summary>
public class BookingServiceSelectionTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();
    private static readonly Guid ServiceC = Guid.NewGuid();

    // ── Validate ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullServiceIds_IsValid_LegacySingleServicePath()
    {
        var result = BookingServiceSelection.Validate(ServiceA, null);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyServiceIds_IsValid_LegacySingleServicePath()
    {
        var result = BookingServiceSelection.Validate(ServiceA, []);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SixServices_IsInvalid_MaxIsFive()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();

        var result = BookingServiceSelection.Validate(ids[0], ids);

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("5");
    }

    [Fact]
    public void Validate_FiveServices_IsValid_MaxIsInclusive()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        var result = BookingServiceSelection.Validate(ids[0], ids);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DuplicateServiceIds_IsInvalid()
    {
        var result = BookingServiceSelection.Validate(ServiceA, [ServiceA, ServiceB, ServiceA]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(BookingServiceSelection.ErrorKind.Duplicate);
    }

    [Fact]
    public void Validate_ServiceIdNotFirstInServiceIds_IsInvalid()
    {
        var result = BookingServiceSelection.Validate(ServiceA, [ServiceB, ServiceA]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(BookingServiceSelection.ErrorKind.FirstMismatch);
    }

    [Fact]
    public void Validate_ServiceIdMatchesFirst_IsValid()
    {
        var result = BookingServiceSelection.Validate(ServiceA, [ServiceA, ServiceB]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NeitherServiceIdNorServiceIds_IsInvalid()
    {
        // GET /api/bookings/slots and /availability require at least one of the two
        // (API_CONTRACT_CYCLE6.md §41.1); POST /api/bookings always sends serviceId so never hits this.
        var result = BookingServiceSelection.Validate(null, null);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(BookingServiceSelection.ErrorKind.MissingBoth);
    }

    [Fact]
    public void Validate_OnlyServiceIds_NoServiceId_IsValid_SlotsAvailabilityShape()
    {
        var result = BookingServiceSelection.Validate(null, [ServiceA, ServiceB]);

        result.IsValid.Should().BeTrue();
    }

    // ── Resolve ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoServiceIds_ReturnsSingleLegacyId()
    {
        var resolved = BookingServiceSelection.Resolve(ServiceA, null);

        resolved.Should().Equal(ServiceA);
    }

    [Fact]
    public void Resolve_WithServiceIds_ReturnsThemInOrder()
    {
        var resolved = BookingServiceSelection.Resolve(ServiceA, [ServiceA, ServiceB, ServiceC]);

        resolved.Should().Equal(ServiceA, ServiceB, ServiceC);
    }

    [Fact]
    public void Resolve_NullServiceId_WithServiceIds_ReturnsServiceIds()
    {
        var resolved = BookingServiceSelection.Resolve(null, [ServiceA, ServiceB]);

        resolved.Should().Equal(ServiceA, ServiceB);
    }

    // ── Aggregate ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_SumsPriceAndDuration_AcrossAllServices()
    {
        var servicesById = new Dictionary<Guid, Service>
        {
            [ServiceA] = new() { Id = ServiceA, DurationMinutes = 60, Price = 2000m },
            [ServiceB] = new() { Id = ServiceB, DurationMinutes = 30, Price = 2500m },
        };

        var (totalDuration, totalPrice, ordered) = BookingServiceSelection.Aggregate([ServiceA, ServiceB], servicesById);

        totalDuration.Should().Be(90);
        totalPrice.Should().Be(4500m);
        ordered.Select(s => s.Id).Should().Equal(ServiceA, ServiceB);
    }

    [Fact]
    public void Aggregate_SingleService_MatchesThatServiceExactly_NoSurpriseForLegacyPath()
    {
        var servicesById = new Dictionary<Guid, Service> { [ServiceA] = new() { Id = ServiceA, DurationMinutes = 45, Price = 1500m } };

        var (totalDuration, totalPrice, ordered) = BookingServiceSelection.Aggregate([ServiceA], servicesById);

        totalDuration.Should().Be(45);
        totalPrice.Should().Be(1500m);
        ordered.Should().ContainSingle().Which.Id.Should().Be(ServiceA);
    }

    [Fact]
    public void Aggregate_PreservesRequestOrder_NotDictionaryOrder()
    {
        var servicesById = new Dictionary<Guid, Service>
        {
            [ServiceA] = new() { Id = ServiceA, DurationMinutes = 60, Price = 1000m },
            [ServiceB] = new() { Id = ServiceB, DurationMinutes = 30, Price = 500m },
            [ServiceC] = new() { Id = ServiceC, DurationMinutes = 15, Price = 250m },
        };

        var (_, _, ordered) = BookingServiceSelection.Aggregate([ServiceC, ServiceA, ServiceB], servicesById);

        ordered.Select(s => s.Id).Should().Equal(ServiceC, ServiceA, ServiceB);
    }
}
