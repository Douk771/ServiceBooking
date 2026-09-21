using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Legal;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// The public intake for a data subject's request under 152-ФЗ — US-74, ARCHITECTURE_CYCLE5.md §50.1.
/// Anonymous by design (a subject may not have — or want — an account); the one hard rule this whole
/// controller exists to enforce is that the response reveals NOTHING about whether the phone is known
/// to the system (§50.1's own emphasis, "проверяется тестом, сравнивающим ответ... байт в байт").
/// </summary>
[ApiController]
[Route("api/subject-requests")]
public class SubjectRequestsController(
    AppDbContext db, CaptchaService captchaService, IOptions<SubjectRequestOptions> options) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("subject-request")]
    public async Task<ActionResult<SubjectRequestAcceptedDto>> Submit([FromBody] SubmitSubjectRequestDto dto)
    {
        if (!Enum.TryParse<SubjectRequestKind>(dto.Kind, ignoreCase: true, out var kind))
            return BadRequest("Укажите тип обращения.");
        if (!PhoneNormalizer.TryNormalize(dto.Phone, out var canonicalPhone))
            return BadRequest("Укажите корректный номер телефона.");
        if (string.IsNullOrWhiteSpace(dto.ContactValue))
            return BadRequest("Укажите контакт для ответа.");
        // Code review, "заодно": ContactValue's column is string(200) (§44.4) but, unlike Message below,
        // had no corresponding length check — an over-length value reached SaveChangesAsync and failed
        // there as a raw DbUpdateException (500), which is also a response-shape a caller could use to
        // distinguish "over 200 chars" from every other 400 this endpoint returns — the one thing §50.1
        // insists must never be distinguishable by response shape.
        if (dto.ContactValue.Length > 200)
            return BadRequest("Контакт для ответа не должен превышать 200 символов.");
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest("Опишите обращение.");
        if (dto.Message.Length > 4000)
            return BadRequest("Текст обращения не должен превышать 4000 символов.");

        if (captchaService.IsEnforced)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!await captchaService.ValidateAsync(dto.CaptchaToken, ip))
                return BadRequest("Проверка на робота не пройдена.");
        }

        var responseDueByWorkingDays = options.Value.ResponseWorkingDays;
        var nowUtc = DateTime.UtcNow;

        // Reference uniqueness by retry, not a DB constraint check-then-insert race guard — a collision
        // among 6 random characters from a 31-symbol alphabet is astronomically rare (31^6 ≈ 887M), this
        // loop exists only so a freak collision doesn't 500 instead of quietly trying again.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var reference = SubjectRequestReference.Generate();
            if (await db.SubjectRequests.AnyAsync(r => r.Reference == reference)) continue;

            db.SubjectRequests.Add(new SubjectRequest
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                Kind = kind,
                SubjectPhone = canonicalPhone,
                ContactValue = dto.ContactValue,
                Message = dto.Message,
                Status = SubjectRequestStatus.Received,
                ReceivedAtUtc = nowUtc,
                DueAtUtc = WorkingDays.Add(nowUtc, responseDueByWorkingDays),
            });
            await db.SaveChangesAsync();

            // 🔴 §50.1: this exact shape, unconditionally — no branch anywhere above this point may ever
            // depend on whether SubjectPhone/ContactValue matched an existing account or a prior request.
            return Accepted(new SubjectRequestAcceptedDto(reference, responseDueByWorkingDays));
        }

        // Practically unreachable (see the astronomical-odds comment above) — but a request that can't be
        // durably recorded must not silently claim success either.
        return StatusCode(StatusCodes.Status503ServiceUnavailable, "Не удалось принять обращение, попробуйте ещё раз.");
    }
}

/// <summary>ARCHITECTURE_CYCLE5.md §57 config — Q-L12's default of 10 working days.</summary>
public sealed class SubjectRequestOptions
{
    public const string SectionName = "SubjectRequests";
    public int ResponseWorkingDays { get; set; } = 10;
}
