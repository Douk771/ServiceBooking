using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;
/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the member endpoints of the former <c>CompaniesController</c>
/// (<c>api/Companies/{id}/members…</c>) — the same resolved prefix (<c>[controller]</c> spelled out), the same
/// per-action [Authorize]/[RequiresOwnerTerms], the same routes.
/// </summary>
[ApiController]
[Route("api/Companies")]
public class CompanyMembersController(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    ServiceBooking.API.Services.Billing.AccountUsageReader accountUsageReader) : ControllerBase
{
    [HttpGet("{id:guid}/members")]
    [Authorize]
    public async Task<ActionResult<List<MemberDto>>> GetMembers(Guid id, CancellationToken ct)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var members = await db.CompanyMembers
            .Include(cm => cm.User)
            .Where(cm => cm.CompanyId == id)
            .ToListAsync(ct);

        var userIds = members.Select(m => m.UserId).ToList();
        var masterServices = await db.MasterServices
            .Where(ms => userIds.Contains(ms.MasterId))
            .ToListAsync(ct);

        var result = members.Select(cm => new MemberDto(
            cm.Id, cm.UserId, cm.User.FirstName, cm.User.LastName,
            cm.User.PhoneNumber ?? "", cm.User.Email, cm.User.AvatarUrl, cm.Role.ToString(), cm.Bio,
            masterServices.Where(ms => ms.MasterId == cm.UserId).Select(ms => ms.ServiceId).ToList(),
            cm.CommissionPercent,
            cm.ProvidesServices
        )).ToList();

        return Ok(result);
    }

    /// <summary>
    /// US-62 (ARCHITECTURE_CYCLE6.md §40.3): only this company's owner (or SuperAdmin) may flip the
    /// flag, and never for themselves via this endpoint's caller — a master can't hide themselves, and
    /// turning the flag off with future bookings requires an explicit confirm.
    /// </summary>
    [HttpPut("{id:guid}/members/{memberId:guid}/provides-services")]
    [Authorize]
    public async Task<IActionResult> UpdateProvidesServices(Guid id, Guid memberId, [FromBody] ProvidesServicesDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();
        // §389.2: a shop has no services/commission — rights first, kind second.
        if (await CompanyKindGuard.RejectShopAsync(db, id) is { } shopRefusal) return shopRefusal;

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        if (!dto.ProvidesServices && !dto.Confirm)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var futureBookingsCount = await db.Bookings.CountAsync(b =>
                b.CompanyId == id && b.MasterId == member.UserId &&
                b.Date >= today && b.Status != BookingStatus.Cancelled);

            if (futureBookingsCount > 0)
            {
                return Conflict(
                    $"У специалиста {futureBookingsCount} будущие записи. Они останутся в силе и в расписании, " +
                    "но клиенты перестанут видеть его при записи. Повторите с подтверждением.");
            }
        }

        member.ProvidesServices = dto.ProvidesServices;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:guid}/members/{memberId:guid}/services")]
    [Authorize]
    public async Task<IActionResult> UpdateMemberServices(Guid id, Guid memberId, [FromBody] List<Guid> serviceIds)
    {
        if (!await CanManageCompany(id)) return Forbid();
        // §389.2: a shop has no services/commission — rights first, kind second.
        if (await CompanyKindGuard.RejectShopAsync(db, id) is { } shopRefusal) return shopRefusal;

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        var requested = serviceIds.Distinct().ToList();

        // Remove existing services for this master that belong to this company
        var companyServiceIds = await db.Services
            .Where(s => s.CompanyId == id)
            .Select(s => s.Id)
            .ToListAsync();

        // Every requested id must belong to THIS company — checked before any write (US-16, audit B2)
        // so a bad id can't leave the link table half-updated. A stray Guid that matches nothing would
        // otherwise slip through Contains() checks below and either silently vanish or (for an id from
        // another company) attach a master to a service that isn't theirs to serve.
        if (requested.Any(sid => !companyServiceIds.Contains(sid)))
            return BadRequest("One or more services do not belong to this company.");

        var existing = await db.MasterServices
            .Where(ms => ms.MasterId == member.UserId && companyServiceIds.Contains(ms.ServiceId))
            .ToListAsync();

        db.MasterServices.RemoveRange(existing);

        foreach (var sid in requested)
            db.MasterServices.Add(new MasterService { Id = Guid.NewGuid(), MasterId = member.UserId, ServiceId = sid });

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/members")]
    [Authorize]
    [RequiresOwnerTerms]
    public async Task<ActionResult<MemberDto>> AddMember(Guid id, AddMemberDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        // Validate that the caller is allowed to assign the requested role
        if (!await CanAssignRole(dto.Role)) return Forbid();

        // SuperAdmin's CanAssignRole above accepts any string sight unseen — so a typo'd role name from
        // a SuperAdmin caller used to sail through and blow up Enum.Parse<UserRole> below with an
        // unhandled 500 (audit D3). Reject an unknown role name explicitly instead.
        if (!Enum.TryParse<UserRole>(dto.Role, out _)) return BadRequest("Unknown role");

        // §389.2/§408.7: a shop has staff only — no co-owners through the API in cycle 1 (Master = "Сотрудник").
        // Applies to SuperAdmin as well; the kind check is a product rule, not a permission.
        var companyKind = await db.Companies.AsNoTracking().Where(c => c.Id == id).Select(c => (CompanyKind?)c.Kind).FirstOrDefaultAsync();
        if (companyKind == CompanyKind.Orders && dto.Role != nameof(UserRole.Master))
            return BadRequest("В магазин можно добавить только сотрудника.");

        // Tariff seat limit (ARCHITECTURE_CYCLE7.md §46.4): SUMMED across every company on the
        // account, not just this one — a customer with 3 branches on an 8-seat plan can put all 8
        // anywhere, not 8-per-branch. Only blocks adding NEW members once at/over the cap; existing
        // members are never removed.
        //
        // Serialize concurrent adds ACROSS THE WHOLE ACCOUNT: without a lock keyed on the account (not
        // just this company), two simultaneous adds to two DIFFERENT companies of the same account could
        // each count the same pre-insert account-wide total, both pass the check, and both insert —
        // letting the account end up over the seat limit the plan was supposed to enforce. Falls back to
        // a per-company key only for the (pre-cycle-5-backfill) edge case of a company with no billing
        // account yet.
        var billingAccountId = await db.Companies.Where(c => c.Id == id).Select(c => c.BillingAccountId).FirstOrDefaultAsync();
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        // §52: "billing-account:{accountId}" → "company-members:{companyId}", strictly in that order
        // (account before company) — seats are counted per account but the row is written to a
        // specific company, and taking both locks (not just the account one) closes the same race for
        // members deleted/added directly against this company concurrently with an account-wide count.
        // Falls back to just the per-company key for the (pre-cycle-5-backfill) edge case of a company
        // with no billing account yet.
        if (billingAccountId.HasValue)
            await AdvisoryLock.AcquireAsync(db, $"billing-account:{billingAccountId}");
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);
        if (plan.AccountMaxEmployees.HasValue)
        {
            var seatsUsed = billingAccountId.HasValue
                ? (await accountUsageReader.GetAsync([billingAccountId.Value])).GetValueOrDefault(billingAccountId.Value)?.SeatsUsed ?? 0
                : await db.CompanyMembers.CountAsync(cm => cm.CompanyId == id);
            if (seatsUsed >= plan.AccountMaxEmployees.Value)
            {
                // ARCHITECTURE_CYCLE19.md §384.3/§411: the limit shown is the resolver's own number
                // (tariff field + bonus, AccountLimitFormula) — no more "purchased" breakdown, so all
                // this needs is the current (or fallback Free) plan's name.
                var sub = billingAccountId.HasValue
                    ? await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == billingAccountId.Value)
                    : null;
                // NB-1: must use the SAME "is this subscription usable right now" gate as the limit
                // itself (SubscriptionResolver.Resolve) — otherwise an expired subscription's raw plan
                // name leaks into the 402 text while the limit that was actually enforced came from Free.
                var subUsableNow = SubscriptionUsability.IsUsable(sub, DateTime.UtcNow)
                    && sub.PlanConfig is { IsActive: true };
                var planName = subUsableNow ? sub!.PlanConfig!.Name : "Бесплатный";
                return StatusCode(402, BillingTexts.SeatLimitReached(seatsUsed, planName, plan.AccountMaxEmployees.Value));
            }
        }

        // US-26: search AND auto-create both use the canonical form — otherwise adding a colleague by
        // "8 999..." would silently create a second account for someone already registered as
        // "+7 999...".
        // US-61/Q4 (§48.2 p.3): adding a staff member is a new-data entry point too.
        if (!PhoneNormalizer.TryNormalizeRussian(dto.Phone, out var canonicalPhone))
            return BadRequest("Введите номер телефона в формате +7 (900) 000-00-00");

        // Accounts are identified by phone (UserName == phone), so look the member up by phone.
        var user = await userManager.FindByNameAsync(canonicalPhone);

        if (user is null)
        {
            // Auto-create by phone. Derived temporary password: "Sb" + last 6 digits of the phone,
            // right-padded to at least 8 chars — always contains an upper ('S'), a lower ('b') and
            // digits, satisfying the password policy. The owner must pass this on to the new master.
            // Now derived from the CANONICAL phone (US-26 p.4 note): for a "+7 999..." input the result
            // is unchanged, but for "8 999..." it differs from before this cycle — called out in
            // CHANGELOG.md.
            var tail = canonicalPhone.Length >= 6 ? canonicalPhone[^6..] : canonicalPhone;
            var pwd = $"Sb{tail}".PadRight(8, '0');
            user = new AppUser
            {
                UserName = canonicalPhone,
                PhoneNumber = canonicalPhone,
                // Cycle 14 review, blocker 3: PhoneNumberConfirmed is now a PUBLIC "phoneVerified"
                // signal (ProfileDto, AuthResponseDto, MasterClientDto) backed by an actual MAX
                // verification (PhoneVerificationWriter is its only other writer). An owner typing in a
                // colleague's phone number is not a verification of anything — leave this false; the
                // badge only lights up once the person verifies through MAX themselves.
                Email = dto.Email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
            };
            var createResult = await userManager.CreateAsync(user, pwd);
            if (!createResult.Succeeded)
                return BadRequest(createResult.Errors.Select(e => e.Description));
        }

        var exists = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == id && cm.UserId == user.Id);
        if (exists) return Conflict("User is already a member");

        var role = Enum.Parse<UserRole>(dto.Role);
        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = id,
            UserId = user.Id,
            Role = role,
            Bio = dto.Bio
        };

        db.CompanyMembers.Add(member);

        await db.SaveChangesAsync();
        // US-46: same "membership change → SaveChangesAsync → SyncAsync → commit" order as Create.
        await IdentityRoleSync.SyncAsync(db, userManager, user.Id);
        await limitTransaction.CommitAsync();

        // New members always start at 0 commission on this membership — same as before, just no longer
        // sourced from a value that could carry over from a different company (US-15).
        return Ok(new MemberDto(member.Id, user.Id, user.FirstName, user.LastName,
            user.PhoneNumber ?? "", user.Email, user.AvatarUrl, dto.Role, dto.Bio, [], member.CommissionPercent,
            member.ProvidesServices));
    }

    [HttpPut("{id:guid}/members/{memberId:guid}/commission")]
    [Authorize]
    public async Task<IActionResult> UpdateMemberCommission(Guid id, Guid memberId, [FromBody] UpdateMemberCommissionDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();
        // §389.2: a shop has no services/commission — rights first, kind second.
        if (await CompanyKindGuard.RejectShopAsync(db, id) is { } shopRefusal) return shopRefusal;

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        member.CommissionPercent = Math.Clamp(dto.CommissionPercent, 0, 100);
        await db.SaveChangesAsync();

        return Ok(new { member.UserId, member.CommissionPercent });
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [Authorize]
    [RequiresOwnerTerms]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        var removedUserId = member.UserId;

        // US-46 p.5, ARCHITECTURE.md §8.4: this is the point that used to leave a stale Identity role
        // behind entirely (removing a member never revoked Master/CompanyOwner) — the lock is the same
        // one AddMember already takes for the seat-limit check, extended here for the first time to
        // RemoveMember.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        db.CompanyMembers.Remove(member);
        await db.SaveChangesAsync();
        await IdentityRoleSync.SyncAsync(db, userManager, removedUserId);
        await transaction.CommitAsync();

        return NoContent();
    }
    // TD-11 (ARCHITECTURE_CYCLE16.md §254): delegates to the single shared implementation.
    // superAdminBypass stays true — this controller's existing behavior.
    private Task<bool> CanManageCompany(Guid companyId) =>
        CompanyAccess.CanManageCompanyAsync(db, User, companyId);


    // SuperAdmin can assign any role; CompanyOwner can assign Master or CompanyOwner only.
    private Task<bool> CanAssignRole(string role)
    {
        if (User.IsInRole("SuperAdmin")) return Task.FromResult(true);
        var allowed = new[] { "Master", "CompanyOwner" };
        return Task.FromResult(allowed.Contains(role));
    }
}
