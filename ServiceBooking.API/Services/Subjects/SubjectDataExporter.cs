using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the body of <c>GET /api/profile/export</c> (US-38, the
/// data subject's copy of their own data), moved verbatim out of <c>ProfileController.Export</c> — same
/// queries, same order, same SUBJECT-PHONE-GATE predicates, same guest-data-gate log line. The controller
/// keeps the route, the rate limit, the Content-Disposition header and the 404/200 mapping.
/// The logger keeps the <c>ProfileController</c> category the gate line was always written under.
/// </summary>
public sealed class SubjectDataExporter(
    UserManager<AppUser> userManager, AppDbContext db, ConsentLedger ledger, SubjectScopeResolver subjectScopeResolver,
    HealthNoteProtector healthNoteProtector, LegalDocumentProvider legalProvider, ILogger<ProfileController> logger,
    GuestDataGateJournal guestDataGateJournal, ServiceBooking.API.Services.Shops.ShopGateLoader gates)
{
    /// <summary>The export for <paramref name="userId"/>, or null when the account does not exist (404).
    /// <paramref name="requestAborted"/> is what the scope resolver was always given
    /// (<c>HttpContext.RequestAborted</c>); <paramref name="ct"/> is the action's own token.
    /// <paramref name="traceId"/> is the request's <c>HttpContext.TraceIdentifier</c>, written to the
    /// guest-data-gate journal (ARCHITECTURE_CYCLE20.md §406.2).</summary>
    public async Task<ProfileExportDto?> ExportAsync(string userId, string? traceId, CancellationToken requestAborted, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;

        // ARCHITECTURE_CYCLE5.md §44.2/§50.2: reads the journal, not a "current state" row — the FULL
        // history for this subject, exactly what GET /api/profile/consents' `history` field shows,
        // because an export is a legal artifact and a revoked/superseded grant is still something the
        // subject did.
        // knownPhone (code review В3): pulls in salon-recorded PhotoConsent/HealthDataConsent rows too —
        // see ConsentLedger.HistoryAsync's doc comment.
        var consentHistory = await ledger.HistoryAsync(ConsentSubject.ForUser(userId), user.PhoneNumber);
        var consent = consentHistory.Select(ToExportConsentDto).ToList();

        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId)
            .Select(cm => new ExportMembershipDto(cm.Company.Name, cm.Role.ToString(), cm.JoinedAt))
            .ToListAsync(ct);

        // Matches DeleteAccount's step 3 and MastersController.GetClients: a visit made as a guest
        // BEFORE this person registered, on the same canonical phone, is data about this subject just
        // as much as a visit made while logged in — DeleteAccount already anonymizes both branches, so
        // the export must show both too, or a deletion could erase data the export never revealed
        // (API_CONTRACT.md §8).
        // TD-03 (ARCHITECTURE_CYCLE16.md §245): the single gate. `ownPhone` is the account's own
        // contact — always shown back to the account (ExportProfileDto below). `guestMatchPhone` is
        // null unless this account has PROVEN it owns that number (VerifiedPhones) — every predicate
        // below that used to read a single ambiguous `canonicalPhone` now reads exactly one of the two,
        // per §245.4's distribution table.
        var scope = await subjectScopeResolver.ForAccountAsync(user, requestAborted);
        var ownPhone = scope.OwnPhone;
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — canonical guard for the whole export
        var bookings = await db.Bookings
            .Include(b => b.Service).Include(b => b.Master).Include(b => b.Company)
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
            .Select(b => new ExportBookingDto(
                b.Id, b.Date, b.StartTime, b.EndTime, b.Company.Name, b.Service.Name,
                b.Master.FirstName + " " + b.Master.LastName, b.Status.ToString(), b.PaymentStatus.ToString(),
                b.Price, b.CancellationReason))
            .ToListAsync(ct);

        var reviews = await db.Reviews
            .Include(r => r.Company)
            .Where(r => r.ClientId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ExportReviewDto(r.Company.Name, r.Rating, r.Comment, r.CreatedAt))
            .ToListAsync(ct);

        // Notes/photos "about me" are found the same way DeleteAccount and MastersController.GetClients
        // do: by ClientId for a registered client, or by canonical GuestPhone for visits made before
        // registering (a client can be both, if they booked as a guest before signing up).
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Company).Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNoteMetaDto(n.Company.Name, n.CreatedAt, n.Photos.Count))
            .ToListAsync(ct);

        var photosOfMe = await db.ClientNotePhotos
            .Include(p => p.ClientNote).ThenInclude(n => n.Company)
            .Where(p => p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ExportPhotoMetaDto(p.ClientNote.Company.Name, p.CreatedAt, p.SizeBytes))
            .ToListAsync(ct);

        // T5-B11 (ARCHITECTURE_CYCLE5.md §50.2, API_CONTRACT_CYCLE5.md §49). "Без N+1": three cheap
        // id-only queries (one per source — bookings/notes/photos already scoped to this subject exactly
        // like the sections above) build BOTH the company-id union and the "what is stored" tags in one
        // pass, then ONE second query fetches the company cards themselves — never one query per company.
        var bookingCompanyIds = await db.Bookings
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(b => b.CompanyId).Distinct().ToListAsync(ct);
        var noteCompanyIds = await db.ClientNotes
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(n => n.CompanyId).Distinct().ToListAsync(ct);
        var photoCompanyIds = await db.ClientNotePhotos
            .Where(p => p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(p => p.CompanyId).Distinct().ToListAsync(ct);
        // Code review В4: was ClientId-only, unlike every neighboring section above — a health note filed
        // while this person was still a guest (booked, then registered later) is stored by GuestPhone,
        // exactly like ClientNote/ClientNotePhoto, and the export silently omitted it.
        var healthNoteRows = await db.ClientHealthNotes
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync(ct);

        // ARCHITECTURE_CYCLE23.md §398.1: orders of the account, and guest orders on the same number ONLY when the number is verified.
        var orderRows = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery()
            .Where(o => o.CustomerUserId == userId || (guestMatchPhone != null && o.CustomerKind == OrderActorKind.Guest && o.CustomerPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — cycle 23, orders follow the same gate as bookings (ARCHITECTURE_CYCLE23.md §398.1)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);

        var whatIsStoredByCompany = new Dictionary<Guid, List<string>>();
        void Tag(IEnumerable<Guid> companyIds, string kind)
        {
            foreach (var id in companyIds)
            {
                if (!whatIsStoredByCompany.TryGetValue(id, out var list)) whatIsStoredByCompany[id] = list = [];
                if (!list.Contains(kind)) list.Add(kind);
            }
        }
        Tag(bookingCompanyIds, "bookings");
        Tag(noteCompanyIds, "notes");
        Tag(photoCompanyIds, "photos");
        Tag(healthNoteRows.Select(n => n.CompanyId), "healthNotes");
        Tag(orderRows.Select(o => o.CompanyId).Distinct(), "orders");

        var operatorCompanyIds = whatIsStoredByCompany.Keys.ToList();
        var operators = operatorCompanyIds.Count == 0 ? []
            : await db.Companies.AsNoTracking().Where(c => operatorCompanyIds.Contains(c.Id))
                .Select(c => new ExportOperatorDto(c.Id, c.Name, c.Address, c.Phone, c.Email, whatIsStoredByCompany[c.Id]))
                .ToListAsync(ct);

        // US-75 — sent notifications and opt-out status. bodyAvailable reflects §51's затирание: a row
        // past its retention window has ContentRedactedAtUtc set, and the export must say so plainly
        // rather than showing an empty string that looks like "nothing was ever sent".
        // Cycle 24 (§461): messages about the subject's ORDERS belong here too — including guest orders on the verified number, which the order gate above
        // already admitted (their rows carry no account id).
        var exportedOrderIds = orderRows.Select(o => o.Id).ToList();
        var notifications = await db.OutboundNotifications.AsNoTracking()
            .Include(n => n.Company)
            .Where(n => n.RecipientUserId == userId || (n.OrderId != null && exportedOrderIds.Contains(n.OrderId.Value)))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNotificationDto(
                n.SentAtUtc, n.Type.ToString(), n.Status.ToString(), n.Company.Name, n.ContentRedactedAtUtc == null))
            .ToListAsync(ct);
        var optOutRow = guestMatchPhone is null ? null
            : await db.NotificationOptOuts.AsNoTracking().FirstOrDefaultAsync(o => o.Phone == guestMatchPhone, ct);  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var optOut = new ExportOptOutDto(optOutRow is not null, optOutRow?.OptedOutAtUtc);

        // US-77 — decrypted explicitly (HealthNoteProtector's own doc comment: never via a transparent
        // converter), same subject-key convention ClientConsentsController uses (userId as SubjectKey for
        // a registered client — this export only ever runs for the account holder themselves).
        var companyNameById = operators.ToDictionary(o => o.CompanyId, o => o.Name);
        var healthNotesExport = new List<ExportHealthNoteDto>();
        foreach (var note in healthNoteRows)
        {
            var value = healthNoteProtector.Unprotect(note.Ciphertext, note.CompanyId, userId);
            healthNotesExport.Add(new ExportHealthNoteDto(companyNameById.GetValueOrDefault(note.CompanyId, ""), value, note.UpdatedAt));
        }

        // ARCHITECTURE_CYCLE14.md §151.3, API_CONTRACT_CYCLE14.md §170.3: only the fact/method/date of
        // verification for the CURRENT number — read straight from VerifiedPhone (source of truth) rather
        // than trusting the mirror alone, since the mirror could in principle drift. The MAX-account
        // identifier (even hashed) and any open session are deliberately excluded — neither is data the
        // subject can meaningfully read back, and an open session lives minutes anyway.
        var verifiedPhoneRow = guestMatchPhone is null ? null
            : await db.VerifiedPhones.AsNoTracking().FirstOrDefaultAsync(v => v.Phone == guestMatchPhone, ct);  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var phoneVerification = new ExportPhoneVerificationDto(
            verifiedPhoneRow is not null, verifiedPhoneRow?.Method.ToString(), verifiedPhoneRow?.VerifiedAtUtc);

        // TD-03 (ARCHITECTURE_CYCLE16.md §245.6, API_CONTRACT_CYCLE16.md §273.1). §272/A2: this section
        // MUST NOT become an oracle for "does this phone have guest data" — it reports only that the
        // rule was applied, never a count or a yes/no about hidden rows. `applied` is derived from
        // `scope.GateApplied` alone, never from whether any of the sections above turned out empty.
        var guestDataGate = new ExportGuestDataGateDto(
            scope.GateApplied,
            scope.GateApplied ? "PhoneNotVerified" : null,
            scope.GateApplied ? SubjectGateTexts.Resolve(legalProvider.Current) : null,
            // BACKEND DEVIATION FROM ARCHITECTURE_CYCLE16.md §245.6/§273.1/§277: those sections write
            // "/subject-request" throughout, but the route that has actually existed in the app since
            // cycle 5 is "/data-request" (frontend/src/App.tsx, SubjectRequestPage.tsx) — "/subject-request"
            // does not exist and would 404. A prior partial run of this cycle already found and fixed
            // this on the frontend side (commit f94a052, useExportData.test.tsx) and flagged the
            // contract/architecture docs as still needing the same fix from architect. Backend here
            // follows the real, working route rather than reproducing the doc's typo. See report.
            scope.GateApplied ? "/data-request" : null);

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/export");
            // ARCHITECTURE_CYCLE20.md §406.2 (US-20-05) — before this method opens any unit of work.
            await guestDataGateJournal.RecordAsync(userId, GuestDataGateOperation.Export, traceId);
        }

        // ARCHITECTURE_CYCLE5.md §50.2, API_CONTRACT_CYCLE5.md §49: "признаны результатом работы салона"
        // is removed — it is not a valid ground for refusal (ч. 8 ст. 14 152-ФЗ is exhaustive). Replaced
        // with routing to the actual operator of that data — see `operators` above.
        var orderShopIds = orderRows.Select(o => o.CompanyId).Distinct().ToList();
        var orderShops = orderShopIds.Count == 0 ? new Dictionary<Guid, Company>()
            : await db.Companies.AsNoTracking().Where(c => orderShopIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var orderSellers = orderShopIds.Count == 0 ? new Dictionary<Guid, ShopSettings>()
            : await db.ShopSettings.AsNoTracking().Where(s => orderShopIds.Contains(s.CompanyId)).ToDictionaryAsync(s => s.CompanyId, ct);
        var pushRows = exportedOrderIds.Count == 0
            ? []
            : await db.OrderPushSubscriptions.AsNoTracking().Where(s => exportedOrderIds.Contains(s.OrderId))
                .Select(s => new { s.OrderId, s.CreatedAtUtc }).ToListAsync(ct);
        var exportNow = DateTime.UtcNow;
        // "Сегодня"/"Завтра" of a pickup are counted from the shop's current WORKING day, as on the storefront (CY24-35).
        var pickupContexts = await gates.PickupContextsAsync(orderShopIds, exportNow, ct);
        var orderExport = orderRows.Select(o =>
        {
            orderShops.TryGetValue(o.CompanyId, out var shop);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop?.TimeZoneId ?? "Europe/Moscow");
            var today = pickupContexts.TryGetValue(o.CompanyId, out var pickupContext)
                ? pickupContext.WorkingDay
                : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(exportNow, zone));
            var orderPush = pushRows.Where(p => p.OrderId == o.Id).Select(p => p.CreatedAtUtc).OrderBy(d => d).ToList();
            orderSellers.TryGetValue(o.CompanyId, out var seller);
            var hasSeller = seller is not null && (!string.IsNullOrWhiteSpace(seller.SellerLegalName) || !string.IsNullOrWhiteSpace(seller.SellerInn));
            return new ExportOrderDto(
                shop?.Name ?? string.Empty, shop?.Address, hasSeller ? new ExportOrderSellerDto(seller!.SellerLegalName, seller.SellerInn) : null,
                o.Number, o.BusinessDate, o.CreatedAtUtc, o.Status.ToString(), OrderTexts.StatusText(o.Status),
                o.CustomerName, o.CustomerPhone, o.Comment,
                o.Items.OrderBy(i => i.Position).Select(i => new ExportOrderItemDto(
                    i.NameSnapshot, i.Unit.ToString(), i.UnitPrice, i.QuantityOrdered, i.QuantityActual, OrderDtoMapper.LineTotal(o, i))).ToList(),
                OrderDtoMapper.DisplayTotal(o), o.StatusReason,
                o.CustomerUserId == userId ? "Account" : "GuestSamePhone",
                // The journal the customer may see: no staff names, no service entries.
                o.Events.Where(e => e.VisibleToCustomer).OrderBy(e => e.OccurredAtUtc)
                    .Select(e => new ExportOrderEventDto(e.OccurredAtUtc, OrderTexts.EventText(e.Kind, e.ToStatus, e.Reason))).ToList(),
                new ExportOrderPickupDto(o.PickupKind.ToString(), o.PickupDate, PickupSchedule.PickupText(o.PickupKind, o.PickupDate, o.PickupStartUtc, zone, exportNow, today)),
                o.NotifyByMessenger, o.MessengerConsentVersion, o.MessengerConsentAtUtc, new ExportOrderPushDto(orderPush.Count, orderPush));
        }).ToList();

        // ARCHITECTURE_CYCLE25.md §504.4, §498.4: the subject's staff MAX link — status and dates only, never the chat id or its key — and the FACT that a
        // shop keeps a note about the number (no text, [legal L17]), only for a PROVEN number (the TD-03 gate: nothing here is an oracle for a stranger's number).
        var staffMaxLink = await db.StaffMaxLinks.AsNoTracking().Where(l => l.UserId == userId)
            .Select(l => new ExportStaffMaxLinkDto(l.Status.ToString(), l.LinkedAtUtc, l.StoppedAtUtc)).FirstOrDefaultAsync(ct);
        var shopCustomerNotes = guestMatchPhone is null
            ? new List<ExportShopCustomerNoteDto>()
            : await db.ShopCustomerNotes.AsNoTracking().Where(n => n.Phone == guestMatchPhone)  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .Join(db.Companies, n => n.CompanyId, c => c.Id, (n, c) => new { c.Name, n.UpdatedAtUtc })
                .OrderBy(x => x.Name).Select(x => new ExportShopCustomerNoteDto(x.Name, x.UpdatedAtUtc)).ToListAsync(ct);

        var export = new ProfileExportDto(
            DateTime.UtcNow,
            // ownPhone (§245.4 table): the account's own contact — shown regardless of verification.
            new ExportProfileDto(user.FirstName, user.LastName, ownPhone, user.Email, user.AvatarUrl, user.CreatedAt),
            consent, memberships, bookings, reviews, notesAboutMe, photosOfMe,
            // Code review, "заодно": names the section by key, not just by description — the reader
            // must not have to guess which of several sections in this same file "compan(ies) below" refers to.
            "Оператором заметок, фотографий и сведений, внесённых сотрудниками компании, является сама " +
            "компания — контакты и адрес каждой такой компании перечислены в разделе «operators» этой " +
            "выгрузки. Запрос об их предоставлении, уточнении или удалении направляйте ей напрямую. По " +
            "вопросам обработки ваших данных платформой обращайтесь в поддержку сервиса.",
            operators, notifications, optOut, healthNotesExport, phoneVerification, guestDataGate, orderExport, staffMaxLink, shopCustomerNotes);

        return export;
    }

    /// <summary>One consent-journal row as the export (and <c>GET /api/profile/consents</c>' history) shows
    /// it — shared by both, hence here rather than duplicated (§385).</summary>
    internal static ExportConsentDto ToExportConsentDto(ConsentState s) =>
        new(s.Id, s.DocumentKey, s.DocumentVersion, s.Purpose?.ToString(), s.Act.ToString(), s.Source.ToString(),
            s.CompanyId, s.GrantedAtUtc, s.RevokedAtUtc, s.RevokeReason);
}
