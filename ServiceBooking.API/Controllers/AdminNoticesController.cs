using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using System.Security.Claims;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404 (US-20-03, Т20-02), API_CONTRACT_CYCLE20.md §434.4–§434.7 — the SuperAdmin
/// side of platform notices: list with addressee/read counters, publish (matrix §434.5), dry-run preview
/// (identical validator, writes nothing), revoke, and the admin's own copy of the attachment endpoint.
/// There is deliberately no PUT — a published notice is never edited (§443), only ever revoked.
/// </summary>
[ApiController]
[Route("api/admin/notices")]
[Authorize(Roles = "SuperAdmin")]
public class AdminNoticesController(AppDbContext db, PlatformNoticePublisher publisher, NoticeAudienceCounter audienceCounter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminPlatformNoticeDto>>> GetNotices(
        [FromQuery] PlatformNoticeKind? kind, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.PlatformNotices.AsNoTracking().AsQueryable();
        if (kind is not null) query = query.Where(n => n.Kind == kind);

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(n => n.PublishedAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync();

        var items = new List<AdminPlatformNoticeDto>(rows.Count);
        foreach (var n in rows)
            items.Add(await MapToAdminDtoAsync(n));

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    [HttpPost]
    public async Task<ActionResult<AdminPlatformNoticeDto>> Publish([FromBody] PlatformNoticeCreateInputDto dto)
    {
        var buildRequest = ToBuildRequest(dto);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (notice, error) = await publisher.TryBuildAsync(buildRequest, userId, DateTime.UtcNow);
        if (error is not null) return BadRequest(error);

        db.PlatformNotices.Add(notice!);
        await db.SaveChangesAsync();

        Response.Headers.Location = "/api/admin/notices";
        return StatusCode(StatusCodes.Status201Created, await MapToAdminDtoAsync(notice!));
    }

    [HttpPost("preview")]
    public async Task<ActionResult<PlatformNoticePreviewDto>> Preview([FromBody] PlatformNoticeCreateInputDto dto)
    {
        var buildRequest = ToBuildRequest(dto);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // Preview writes nothing — TryBuildAsync itself never calls SaveChangesAsync, only the caller
        // (Publish, above) does.
        var (notice, error) = await publisher.TryBuildAsync(buildRequest, userId, DateTime.UtcNow);
        if (error is not null) return BadRequest(error);

        var audienceCount = await audienceCounter.CountAsync(notice!.AudienceType, notice.AudiencePlanIds, notice.TargetBillingAccountId);
        return Ok(new PlatformNoticePreviewDto(
            notice.Title, notice.Body, notice.TemplateVersion, notice.EffectiveFrom, notice.VisibleUntilUtc,
            audienceCount, notice.AttachmentSha256));
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<ActionResult<AdminPlatformNoticeDto>> Revoke(Guid id, [FromBody] NoticeRevokeInputDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest("Укажите причину отзыва.");
        if (dto.Reason.Length > PlatformNoticeRules.MaxRevokeReasonLength)
            return BadRequest($"Причина отзыва не должна превышать {PlatformNoticeRules.MaxRevokeReasonLength} символов.");

        var notice = await db.PlatformNotices.FirstOrDefaultAsync(n => n.Id == id);
        if (notice is null) return NotFound();
        if (notice.RevokedAtUtc is not null) return Conflict("Уведомление уже отозвано.");

        notice.RevokedAtUtc = DateTime.UtcNow;
        notice.RevokedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        notice.RevokeReason = dto.Reason;
        await db.SaveChangesAsync();

        return Ok(await MapToAdminDtoAsync(notice));
    }

    // API_CONTRACT_CYCLE20.md §434.7 tail: "то же, что §434.3, для SuperAdmin; 404, если вложения нет" —
    // no audience check (a SuperAdmin may inspect any notice they themselves can publish/revoke).
    [HttpGet("{id:guid}/attachment")]
    public async Task<IActionResult> GetAttachment(Guid id)
    {
        var notice = await db.PlatformNotices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
        if (notice?.AttachmentHtml is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(notice.AttachmentHtml, "text/html; charset=utf-8");
    }

    private static PlatformNoticeBuildRequest ToBuildRequest(PlatformNoticeCreateInputDto dto) => new(
        dto.Kind, dto.Audience.Type, dto.Audience.PlanIds, dto.Audience.BillingAccountId, dto.EffectiveFrom,
        dto.Title, dto.Body, dto.LinkUrl,
        dto.PriceChange is null ? null : new PriceChangeParams(dto.PriceChange.PlanId, dto.PriceChange.OldPricePerMonth, dto.PriceChange.NewPricePerMonth),
        dto.TermsChange is null ? null : new TermsChangeParams(dto.TermsChange.DocumentType, dto.TermsChange.ChangesSummary),
        dto.Attachment is null ? null : new NoticeAttachmentParams(dto.Attachment.Title, dto.Attachment.Html));

    private async Task<AdminPlatformNoticeDto> MapToAdminDtoAsync(PlatformNotice n)
    {
        var createdByName = n.CreatedByUserId == "system" ? "Система" : await ResolveUserNameAsync(n.CreatedByUserId);
        var revokedByName = n.RevokedByUserId is null ? null : await ResolveUserNameAsync(n.RevokedByUserId);
        var audienceCount = await audienceCounter.CountAsync(n.AudienceType, n.AudiencePlanIds, n.TargetBillingAccountId);
        var acknowledgedCount = await db.PlatformNoticeAcknowledgements.CountAsync(a => a.NoticeId == n.Id);

        return new AdminPlatformNoticeDto(
            n.Id, n.Kind.ToString(), n.Title, n.Body, n.LinkUrl, n.EffectiveFrom, n.PublishedAtUtc, n.VisibleUntilUtc,
            n.AttachmentTitle is not null ? new NoticeAttachmentRefDto(n.AttachmentTitle, n.AttachmentSha256!) : null,
            n.AudienceType.ToString(), n.AudiencePlanIds, n.TargetBillingAccountId, n.TemplateVersion,
            createdByName, audienceCount, acknowledgedCount,
            n.RevokedAtUtc, revokedByName, n.RevokeReason);
    }

    private async Task<string> ResolveUserNameAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? userId : $"{user.FirstName} {user.LastName}".Trim();
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────
// Field names/casing match frontend/src/types/api-cycle20.generated.ts's AdminPlatformNoticeDto/
// PlatformNoticeCreateInput/PlatformNoticePreviewDto/NoticeRevokeInput exactly (frontend-developer
// generated these from this cycle's contract ahead of this implementation).

public record NoticeAudienceInputDto(NoticeAudienceType Type, Guid[]? PlanIds, Guid? BillingAccountId);
public record PriceChangeParamsInputDto(Guid PlanId, decimal OldPricePerMonth, decimal NewPricePerMonth);
public record TermsChangeParamsInputDto(LegalDocumentType DocumentType, string ChangesSummary);
public record NoticeAttachmentInputDto(string Title, string Html);

public record PlatformNoticeCreateInputDto(
    PlatformNoticeKind Kind, NoticeAudienceInputDto Audience, DateOnly? EffectiveFrom,
    string? Title, string? Body, string? LinkUrl,
    PriceChangeParamsInputDto? PriceChange, TermsChangeParamsInputDto? TermsChange, NoticeAttachmentInputDto? Attachment);

public record PlatformNoticePreviewDto(
    string Title, string Body, string? TemplateVersion, DateOnly? EffectiveFrom, DateTime VisibleUntil,
    int AudienceCount, string? AttachmentSha256);

public record AdminPlatformNoticeDto(
    Guid Id, string Kind, string Title, string Body, string? LinkUrl, DateOnly? EffectiveFrom,
    DateTime PublishedAt, DateTime VisibleUntil, NoticeAttachmentRefDto? Attachment,
    string AudienceType, Guid[]? AudiencePlanIds, Guid? TargetBillingAccountId, string? TemplateVersion,
    string CreatedByName, int AudienceCount, int AcknowledgedCount,
    DateTime? RevokedAt, string? RevokedByName, string? RevokeReason);

public record NoticeRevokeInputDto(string Reason);
