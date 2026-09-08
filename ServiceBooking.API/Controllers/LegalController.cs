using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/legal")]
public class LegalController(LegalDocumentProvider provider) : ControllerBase
{
    private const string UnavailableMessage = "Правовые документы временно недоступны.";

    // Public. Metadata for both documents — enough for the footer, the registration form and version
    // comparison, without shipping the (potentially large) HTML text (ARCHITECTURE.md §1, API_CONTRACT §1).
    [HttpGet("documents")]
    public ActionResult<LegalDocumentListDto> GetDocuments()
    {
        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        return Ok(new LegalDocumentListDto(snapshot.Documents.Values
            .OrderBy(d => d.Type)
            .Select(MapToMetaDto)
            .ToList()));
    }

    // Public. Metadata AND text for one document.
    [HttpGet("documents/{type}")]
    public ActionResult<LegalDocumentDto> GetDocument(string type)
    {
        if (!Enum.TryParse<LegalDocumentType>(type, ignoreCase: true, out var documentType))
            return NotFound();

        var snapshot = provider.Current;
        if (snapshot is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        var doc = snapshot.Get(documentType);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, UnavailableMessage);

        // ARCHITECTURE.md §2: text changes two or three times over the product's lifetime, and this page
        // is public — five minutes of caching doesn't get in the way of the operational replacement,
        // which is already picked up by the provider within ReloadSeconds.
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(new LegalDocumentDto(
            doc.Type.ToString(), doc.Title, doc.Version, doc.EffectiveFrom, doc.IsDraft,
            doc.ChangeKind.ToString(), doc.ContentHtml));
    }

    private static LegalDocumentMetaDto MapToMetaDto(LegalDocument d) =>
        new(d.Type.ToString(), d.Title, d.Version, d.EffectiveFrom, d.IsDraft, d.ChangeKind.ToString());
}

public record LegalDocumentMetaDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind);

public record LegalDocumentListDto(List<LegalDocumentMetaDto> Documents);

public record LegalDocumentDto(
    string Type, string Title, string Version, DateOnly EffectiveFrom, bool IsDraft, string ChangeKind,
    string ContentHtml);
