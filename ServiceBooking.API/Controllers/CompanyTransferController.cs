using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>contracts/cycle7/openapi.yaml tag company-transfer (US-77, US-64 p.4) — moving a company
/// to another billing account, and the audit trail of who manages it. Wires the already-built
/// <see cref="CompanyTransferService"/>/<see cref="CompanyTransferCalculator"/> to HTTP;
/// <c>PUT /api/admin/companies/{id}/owner</c> itself already lives on <see cref="AdminController"/>
/// (§50, unchanged route/shape, only its behavior changed per the contract — verified, not touched
/// again here).</summary>
[ApiController]
[Route("api/admin/companies")]
[Authorize(Roles = "SuperAdmin")]
public class CompanyTransferController(AppDbContext db, CompanyTransferService transferService, AccountUsageReader usageReader) : ControllerBase
{
    [HttpGet("{companyId:guid}/transfer/preview")]
    public async Task<IActionResult> Preview(Guid companyId, [FromQuery] Guid targetBillingAccountId, [FromQuery] string? newOwnerUserId)
    {
        var company = await db.Companies.FindAsync(companyId);
        if (company is null) return NotFound();

        var result = await transferService.PreviewAsync(companyId, targetBillingAccountId, newOwnerUserId);
        // N8, §51.1 — an invalid new-owner (not found/deleted/not linked) must NOT collapse the whole
        // preview into a zeroed-out "blocked" shape: the transfer of the COMPANY itself (money side) is
        // still perfectly previewable, just without the requested owner change. Only re-run the
        // company-side preview with newOwnerUserId cleared (real numbers, ownerWillChange forced false)
        // and report the owner problem through NewOwner (isValid: false), exactly as the contract's own
        // shape for this requires — never through BlockReason/zeroed limits, which is reserved for an
        // actual company/seat limit block.
        var isNewOwnerFailure = !result.Success && result.Failure!.Kind is
            TransferFailureKind.NewOwnerNotFound or TransferFailureKind.NewOwnerDeleted or TransferFailureKind.NewOwnerNotLinkedToTargetAccount;
        if (!result.Success && !isNewOwnerFailure)
        {
            return result.Failure!.Kind switch
            {
                TransferFailureKind.CompanyNotFound or TransferFailureKind.TargetAccountNotFound => NotFound(),
                _ => Ok(await BuildBlockedPreviewDtoAsync(company, targetBillingAccountId, result.Failure!.Message)),
            };
        }

        var preview = isNewOwnerFailure
            ? (await transferService.PreviewAsync(companyId, targetBillingAccountId, newOwnerUserId: null)).Preview!
            : result.Preview!;
        var sourceSide = await BuildSideDtoAsync(company.BillingAccountId);
        var targetSide = await BuildSideDtoAsync(targetBillingAccountId);
        var seatsOfCompany = (await usageReader.GetCompanySeatsAsync([companyId])).GetValueOrDefault(companyId);

        TransferNewOwnerDto? newOwnerDto = null;
        string? ownerUnchangedNotice = null;
        if (!string.IsNullOrEmpty(newOwnerUserId))
        {
            newOwnerDto = await BuildNewOwnerDtoAsync(companyId, targetBillingAccountId, newOwnerUserId);
        }
        else
        {
            var currentOwnerName = await GetUserDisplayNameAsync(company.OwnerUserId);
            ownerUnchangedNotice = $"Ответственный не меняется: компанией продолжит управлять {currentOwnerName}.";
        }

        var willDetach = await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == companyId);
        var willCancel = await db.OutboundNotifications.CountAsync(n =>
            n.CompanyId == companyId && n.Status == NotificationStatus.Pending);

        var dto = new CompanyTransferPreviewDto(
            company.Id, company.Name, seatsOfCompany, sourceSide, targetSide,
            preview.CompaniesUsedOnTarget, preview.CompanyLimit, preview.SeatsAfter, preview.SeatLimit,
            CanTransfer: !preview.CompanyLimitExceeded,
            BlockReason: preview.CompanyLimitExceeded ? "У принимающего аккаунта нет свободного места в лимите компаний." : null,
            SeatOverflow: preview.SeatOverflow,
            SeatOverflowText: preview.SeatOverflow ? $"После переноса будет занято {preview.SeatsAfter} из {preview.SeatLimit} мест." : null,
            WillDetachFromChannel: willDetach,
            WillCancelPendingNotifications: willCancel,
            NewOwner: newOwnerDto,
            OwnerUnchangedNotice: ownerUnchangedNotice);

        return Ok(dto);
    }

    [HttpPost("{companyId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid companyId, [FromBody] CompanyTransferInput dto)
    {
        var changedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await transferService.TransferAsync(
            companyId, dto.TargetBillingAccountId, dto.NewOwnerUserId, dto.ConfirmSeatOverflow, changedByUserId);

        if (result.Success) return NoContent();

        return result.Failure!.Kind switch
        {
            TransferFailureKind.CompanyNotFound or TransferFailureKind.TargetAccountNotFound => NotFound(),
            TransferFailureKind.NewOwnerNotFound or TransferFailureKind.NewOwnerDeleted =>
                new ContentResult { StatusCode = StatusCodes.Status400BadRequest, Content = result.Failure.Message, ContentType = "text/plain; charset=utf-8" },
            TransferFailureKind.CompanyLimitExceeded =>
                new ContentResult { StatusCode = StatusCodes.Status402PaymentRequired, Content = result.Failure.Message, ContentType = "text/plain; charset=utf-8" },
            _ => new ContentResult { StatusCode = StatusCodes.Status409Conflict, Content = result.Failure.Message, ContentType = "text/plain; charset=utf-8" },
        };
    }

    [HttpGet("{companyId:guid}/owner-history")]
    public async Task<IActionResult> GetOwnerHistory(Guid companyId)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId)) return NotFound();

        var logs = await db.CompanyOwnerChangeLogs.Where(l => l.CompanyId == companyId)
            .OrderByDescending(l => l.ChangedAtUtc).ToListAsync();

        var userIds = logs.SelectMany(l => new[] { l.OldOwnerUserId, l.NewOwnerUserId, l.ChangedByUserId }).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var items = logs.Select(l => new CompanyOwnerChangeDto(
            l.Id, l.ChangedAtUtc, names.GetValueOrDefault(l.ChangedByUserId, l.ChangedByUserId),
            names.GetValueOrDefault(l.OldOwnerUserId, l.OldOwnerUserId), names.GetValueOrDefault(l.NewOwnerUserId, l.NewOwnerUserId),
            l.WithTransfer, l.Comment)).ToList();

        return Ok(new { items });
    }

    private async Task<TransferSideDto> BuildSideDtoAsync(Guid? billingAccountId)
    {
        if (billingAccountId is null) return new TransferSideDto(Guid.Empty, null, null);

        var account = await db.BillingAccounts.FindAsync(billingAccountId.Value);
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == billingAccountId.Value);
        return new TransferSideDto(billingAccountId.Value, account?.Name, sub?.PlanConfig?.Name);
    }

    private async Task<CompanyTransferPreviewDto> BuildBlockedPreviewDtoAsync(Company company, Guid targetBillingAccountId, string blockReason)
    {
        var sourceSide = await BuildSideDtoAsync(company.BillingAccountId);
        var targetSide = await BuildSideDtoAsync(targetBillingAccountId);
        var seatsOfCompany = (await usageReader.GetCompanySeatsAsync([company.Id])).GetValueOrDefault(company.Id);

        return new CompanyTransferPreviewDto(
            company.Id, company.Name, seatsOfCompany, sourceSide, targetSide,
            TargetCompaniesUsed: 0, TargetCompaniesLimit: null, TargetSeatsUsed: 0, TargetSeatsLimit: null,
            CanTransfer: false, BlockReason: blockReason, SeatOverflow: false, SeatOverflowText: null,
            WillDetachFromChannel: false, WillCancelPendingNotifications: 0, NewOwner: null, OwnerUnchangedNotice: null);
    }

    private async Task<TransferNewOwnerDto> BuildNewOwnerDtoAsync(Guid companyId, Guid targetBillingAccountId, string newOwnerUserId)
    {
        var (user, failure) = await transferService.ValidateNewOwnerAsync(newOwnerUserId, targetBillingAccountId);
        if (failure is not null)
        {
            var fallbackName = await GetUserDisplayNameAsync(newOwnerUserId);
            // NB-8 (cycle-07 backend report): every ValidateNewOwnerAsync failure kind means "no usable
            // relation to the target account" per the contract's own TransferOwnerRelation enum
            // (AccountHolder/AccountMember/None) — there is no failure kind that maps to anything but
            // None, so this was never actually a switch on Kind; it's a constant.
            return new TransferNewOwnerDto(newOwnerUserId, fallbackName, false, "None", false, failure.Message);
        }

        var isHolder = await db.BillingAccounts.AnyAsync(a => a.Id == targetBillingAccountId && a.OwnerUserId == newOwnerUserId);
        var isAlreadyMember = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == user!.Id);
        var willOccupySeat = !isAlreadyMember;
        var name = $"{user!.FirstName} {user.LastName}".Trim();
        var oldOwnerName = await GetUserDisplayNameAsync((await db.Companies.Where(c => c.Id == companyId).Select(c => c.OwnerUserId).FirstAsync()));

        // N7, §51.4: CompanyOwnerWriter ALWAYS demotes the previous owner to Master and keeps them on
        // staff (it never removes them) — this notice must say so unconditionally, not only when the
        // new owner doesn't occupy a seat of their own. The two facts are independent: whether the new
        // owner takes a NEW seat is about the new owner; the old owner staying on staff is about the
        // old owner, and happens every time.
        var notice = $"Ответственным станет {name}. {oldOwnerName} останется сотрудником компании.";

        return new TransferNewOwnerDto(user.Id, name, true, isHolder ? "AccountHolder" : "AccountMember", willOccupySeat, notice);
    }

    private async Task<string> GetUserDisplayNameAsync(string userId)
    {
        var user = await db.Users.FindAsync(userId);
        return user is null ? userId : $"{user.FirstName} {user.LastName}".Trim();
    }
}
