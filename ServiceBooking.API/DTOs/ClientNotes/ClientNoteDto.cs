namespace ServiceBooking.API.DTOs.ClientNotes;

public record ClientNotePhotoDto(
    Guid Id,
    // Paths to the protected API endpoint, NOT values for <img src> — the browser won't attach the
    // Authorization header to an <img> request, so the frontend must fetch these as an authenticated
    // blob (API_CONTRACT.md §1.2 p.3, ARCHITECTURE.md §12.2).
    string Url,
    string ThumbnailUrl,
    int Width,
    int Height,
    long SizeBytes,
    DateTime CreatedAt,
    string? UploadedByName,
    bool CanDelete
);

public record ClientNoteDto(
    Guid Id,
    string Note,
    DateTime CreatedAt,
    string AuthorId,
    string AuthorName,
    Guid? BookingId,
    DateOnly? BookingDate,
    string? BookingServiceName,
    bool CanDelete,
    List<ClientNotePhotoDto> Photos
);

public record BookingSummaryDto(DateOnly Date, string ServiceName, string Status);

public record MasterClientDto(
    string? ClientId,
    string? GuestPhone,
    string Name,
    string? Phone,
    string? Email,
    DateOnly LastVisitDate,
    int TotalVisits,
    List<ClientNoteDto> Notes,
    List<BookingSummaryDto> BookingSummaries
);

// Note length/emptiness are checked by hand in the controller, not via [Required]/[MaxLength]: those
// would produce the automatic ValidationProblemDetails body, but API_CONTRACT.md §2.1 specifies plain-
// text 400s ("Note text is required.", "Note must be 2000 characters or fewer.") for this endpoint.
public record AddNoteRequest(
    Guid CompanyId,
    string? ClientId,
    string? GuestPhone,
    string Note,
    // Filled when the note is written from the panel under a booking (US-17 p.4) — see
    // ARCHITECTURE.md §5.1 for why the link lives on the note rather than the photo.
    Guid? BookingId = null
);
