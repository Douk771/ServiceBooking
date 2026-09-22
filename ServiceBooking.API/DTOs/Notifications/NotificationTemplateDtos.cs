namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §29.1's placeholder catalog entries.</summary>
public record TemplatePlaceholderDto(string Token, string Description, IReadOnlyList<string> Types);

public record TemplateItemDto(string Type, string Body, bool IsDefault, string DefaultBody, DateTime? UpdatedAt);

/// <summary>API_CONTRACT_CYCLE5.md §47.1 — GET /api/companies/{id}/notification-templates. `AdMarkers`
/// comes straight from PlatformSetting (T5-B12): the frontend never hardcodes or caches its own copy, so
/// a superadmin edit takes effect for both the client-side hint AND the server's own final check at the
/// same moment.</summary>
public record TemplatesResponseDto(
    IReadOnlyList<TemplatePlaceholderDto> Placeholders, string UnsubscribeLine, IReadOnlyList<TemplateItemDto> Templates,
    IReadOnlyList<string> AdMarkers, string WarningTextKey, string WarningVersion);

/// <summary>ARCHITECTURE_CYCLE5.md §51.1 — never cached/inherited server-side (US-69 п. 4): every save
/// carries its own acknowledgement, checked against the CURRENT uiTexts.TemplateAdWarning version.</summary>
public record TemplateAcknowledgementDto(string? WarningVersion, bool Accepted, bool ConfirmedDespiteMarkers);

/// <summary>API_CONTRACT_CYCLE4.md §29.2/§29.3 request body — shared by PUT and the (cut-fifth) preview.
/// `Acknowledgement` is required on PUT only when actually saving custom text (not on a reset-to-default).</summary>
public record TemplateBodyDto(string Body, TemplateAcknowledgementDto? Acknowledgement = null);

public record TemplatePreviewDto(string Rendered);

/// <summary>ARCHITECTURE_CYCLE5.md §51.2, API_CONTRACT_CYCLE5.md §47.2 — the JSON-bodied 400 when the ad
/// heuristic fires and the owner hasn't (yet) confirmed despite it.</summary>
public record TemplateMarkersHitDto(IReadOnlyList<string> MarkersHit, string Message);
