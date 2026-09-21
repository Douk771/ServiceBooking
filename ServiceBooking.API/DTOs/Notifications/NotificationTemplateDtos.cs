namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §29.1's placeholder catalog entries.</summary>
public record TemplatePlaceholderDto(string Token, string Description, IReadOnlyList<string> Types);

public record TemplateItemDto(string Type, string Body, bool IsDefault, string DefaultBody, DateTime? UpdatedAt);

/// <summary>API_CONTRACT_CYCLE4.md §29.1 — GET /api/companies/{id}/notification-templates.</summary>
public record TemplatesResponseDto(
    IReadOnlyList<TemplatePlaceholderDto> Placeholders, string UnsubscribeLine, IReadOnlyList<TemplateItemDto> Templates);

/// <summary>API_CONTRACT_CYCLE4.md §29.2/§29.3 request body — shared by PUT and the (cut-fifth) preview.</summary>
public record TemplateBodyDto(string Body);

public record TemplatePreviewDto(string Rendered);
