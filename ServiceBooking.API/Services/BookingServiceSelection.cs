using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services;

/// <summary>
/// US-67 (ARCHITECTURE_CYCLE6.md §47.1): pure validation/aggregation for "which services make up this
/// visit", shared by <c>POST /api/bookings</c>, <c>GET /api/bookings/slots</c> and
/// <c>GET /api/bookings/availability</c> so the three can never disagree about totals. No DB access —
/// everything that needs a lookup (existence, company, active flag, master capability) stays in the
/// caller, which already has that data loaded.
/// </summary>
public static class BookingServiceSelection
{
    public const int MaxServicesPerBooking = 5;

    public enum ErrorKind
    {
        None,
        MissingBoth,
        TooMany,
        Duplicate,
        FirstMismatch,
    }

    public readonly record struct ValidationResult(ErrorKind Error)
    {
        public bool IsValid => Error == ErrorKind.None;

        public string Message => Error switch
        {
            ErrorKind.MissingBoth => "Укажите serviceId или serviceIds.",
            ErrorKind.TooMany => $"Максимум {MaxServicesPerBooking} услуг за один визит.",
            ErrorKind.Duplicate => "Услуги в визите не должны повторяться.",
            ErrorKind.FirstMismatch => "serviceId должен совпадать с первой услугой в serviceIds.",
            _ => "",
        };
    }

    /// <summary>
    /// Resolves the effective ordered list of service ids for a booking request. <c>POST /api/bookings</c>
    /// always sends <paramref name="serviceId"/> (non-null); <c>GET /api/bookings/slots</c> and
    /// <c>GET /api/bookings/availability</c> accept either it alone, <paramref name="serviceIds"/> alone,
    /// or both — in which case the first element of <paramref name="serviceIds"/> must match
    /// <paramref name="serviceId"/> (API_CONTRACT_CYCLE6.md §41.1: "обязателен хотя бы один способ").
    /// </summary>
    public static ValidationResult Validate(Guid? serviceId, IReadOnlyList<Guid>? serviceIds)
    {
        if (serviceIds is null || serviceIds.Count == 0)
            return serviceId is null ? new ValidationResult(ErrorKind.MissingBoth) : new ValidationResult(ErrorKind.None);
        if (serviceIds.Count > MaxServicesPerBooking) return new ValidationResult(ErrorKind.TooMany);
        if (serviceIds.Distinct().Count() != serviceIds.Count) return new ValidationResult(ErrorKind.Duplicate);
        if (serviceId is not null && serviceIds[0] != serviceId) return new ValidationResult(ErrorKind.FirstMismatch);
        return new ValidationResult(ErrorKind.None);
    }

    /// <summary>The ordered ids actually used for this booking — collapses the "no serviceIds sent" case
    /// to the single legacy id, so every caller downstream works off one list regardless of which form
    /// the client used. Caller must have already checked <see cref="Validate"/> is valid (at least one
    /// of the two is non-empty).</summary>
    public static List<Guid> Resolve(Guid? serviceId, IReadOnlyList<Guid>? serviceIds) =>
        serviceIds is { Count: > 0 } ? serviceIds.ToList() : [serviceId!.Value];

    /// <summary>Sum of durations/prices for the resolved services, in request order. Caller guarantees
    /// <paramref name="servicesById"/> has an entry for every id in <paramref name="orderedIds"/> — this
    /// just does the arithmetic and preserves order for BookingService.Position.</summary>
    public static (int TotalDurationMinutes, decimal TotalPrice, List<Service> Ordered) Aggregate(
        List<Guid> orderedIds, IReadOnlyDictionary<Guid, Service> servicesById)
    {
        var ordered = orderedIds.Select(id => servicesById[id]).ToList();
        var totalDuration = ordered.Sum(s => s.DurationMinutes);
        var totalPrice = ordered.Sum(s => s.Price);
        return (totalDuration, totalPrice, ordered);
    }
}
