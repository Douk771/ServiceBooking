using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>Outcome of <see cref="AccountDeletionService.DeleteAsync"/> — the controller maps it to the
/// same HTTP results the action always returned (404 / 400 text / 409 text / 204).</summary>
public enum AccountDeletionStatus { Deleted, UserNotFound, WrongPassword, OwnsCompany }

public sealed record AccountDeletionResult(AccountDeletionStatus Status, string? Message = null);

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the bodies of <c>POST /api/profile/delete-account</c> (US-39,
/// the data subject's erasure) and its <c>GET …/preview</c>, moved verbatim out of <c>ProfileController</c> —
/// same gates, same queries, same transaction and advisory-lock order, same "files only after commit" step,
/// same guest-data-gate log line (kept under the <c>ProfileController</c> category).
/// </summary>
public sealed class AccountDeletionService(
    UserManager<AppUser> userManager, AppDbContext db, FileStorage storage, SubjectScopeResolver subjectScopeResolver,
    ILogger<ProfileController> logger, GuestDataGateJournal guestDataGateJournal)
{
    public async Task<AccountDeletionPreviewDto> PreviewAsync(string userId, CancellationToken ct)
    {
        var everHadTrial = await db.BillingAccounts
            .AnyAsync(a => a.OwnerUserId == userId && a.TrialStartedAtUtc != null, ct);
        return new AccountDeletionPreviewDto(
            everHadTrial ? Services.Billing.TrialLegalNotices.TrialRegistryNoticeOnAccountDeletion : null);
    }

    /// <summary><paramref name="requestAborted"/> is what the scope resolver was always given
    /// (<c>HttpContext.RequestAborted</c>). <paramref name="traceId"/> is the request's
    /// <c>HttpContext.TraceIdentifier</c>, written to the guest-data-gate journal (ARCHITECTURE_CYCLE20.md §406.2).</summary>
    public async Task<AccountDeletionResult> DeleteAsync(string userId, string currentPassword, string? traceId, CancellationToken requestAborted)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return new(AccountDeletionStatus.UserNotFound);

        if (!await userManager.CheckPasswordAsync(user, currentPassword))
            return new(AccountDeletionStatus.WrongPassword, "Неверный текущий пароль");

        // Gate #2: a company owner must transfer or close their company first — deleting the account
        // out from under an active business is not this endpoint's job (API_CONTRACT.md §9).
        if (await db.Companies.AnyAsync(c => c.OwnerUserId == userId))
            return new(AccountDeletionStatus.OwnsCompany, "За вами числится компания. Передайте её другому владельцу или обратитесь " +
                             "в поддержку — тогда аккаунт можно будет удалить.");

        // TD-03 (ARCHITECTURE_CYCLE16.md §245): resolved before any field on `user` is scrubbed below.
        // `guestMatchPhone` is null unless this account proved it owns its own number — an unverified
        // account can no longer physically DESTROY someone else's guest-recorded data just by deleting
        // itself. `ownPhone` is captured only for the avatar/file cleanup below, which is this
        // account's own data regardless of verification.
        var scope = await subjectScopeResolver.ForAccountAsync(user, requestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var oldAvatarUrl = user.AvatarUrl;

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/delete-account");
            await guestDataGateJournal.RecordAsync(userId, GuestDataGateOperation.DeleteAccount, traceId);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Step 1 used to delete UserConsent rows outright here. CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md
        // §44.2 p.5, §55.1 R6): the consent journal now OUTLIVES account deletion — a ConsentRecord is
        // evidence the OPERATOR needs to prove what was consented to (ч. 1 ст. 9: "доказывает оператор"),
        // not personal convenience data the subject can erase on demand. All three of ConsentRecord's FKs
        // are NO ACTION specifically so this can't silently regress even if this comment goes stale — the
        // rows physically cannot be removed by cascading `user` below. Retention (§49.1, not built by
        // this task) is what eventually ages them out, at a minimum of three years, never on request.

        // Step 2: notes ABOUT this person (by ClientId, or by guest phone for pre-registration visits) —
        // gather photo storage keys before the cascade delete removes the ClientNotePhoto rows, since
        // the files themselves live outside the database (ARCHITECTURE.md §1.4: DB row goes first, file
        // cleanup happens only after a successful commit).
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone && !n.Company.IsShowcase))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        var photoKeysToDelete = notesAboutMe.SelectMany(n => n.Photos)
            .Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
        db.ClientNotes.RemoveRange(notesAboutMe); // cascades ClientNotePhotos (AppDbContext)

        // Step 2b (code review, Б1): ClientHealthNote is a SEPARATE table from ClientNote (deliberately,
        // §44.3 — no navigation property between them at all), with a NO ACTION FK, so it does not
        // cascade with the ClientNotes.RemoveRange above and was missing here entirely until this fix.
        // Left alone, the row was unreachable by ANY product path after this method ran: ResolveClientAsync
        // (ClientConsentsController) looks the subject up by ClientId OR canonical GuestPhone, and this
        // method has just cleared user.PhoneNumber — so DELETE health-note would 404 forever, and the
        // row would sit until the retention sweep aged it out three years later. Same double condition as
        // notesAboutMe above.
        var healthNotesAboutMe = await db.ClientHealthNotes
            .Where(h => h.ClientId == userId || (guestMatchPhone != null && h.GuestPhone == guestMatchPhone && !h.Company.IsShowcase))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        db.ClientHealthNotes.RemoveRange(healthNotesAboutMe);

        // Step 2c (ARCHITECTURE_CYCLE9.md §105.5, US-123): this person's OWN Web Push subscriptions —
        // ⚠️ the Cascade FK on PushSubscription.UserId (AppDbContext) does NOT fire here: this method
        // leaves a TOMBSTONE (AppUser.DeletedAtUtc is set below; the row itself is never removed), so
        // nothing about this deletion is actually a cascadable "AppUser row went away". Deleted
        // explicitly instead, in the same transaction, same reasoning as I10's owned-channel cleanup
        // further below (a channel this person OWNS is decommissioned explicitly for the identical
        // reason). A person who deletes their account must stop receiving push about a platform they no
        // longer have credentials to see.
        var ownPushSubscriptions = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        db.PushSubscriptions.RemoveRange(ownPushSubscriptions);

        // Step 2d (ARCHITECTURE_CYCLE14.md §151.2, US-14-12): same reasoning as step 2c — the Cascade FK
        // on VerifiedPhone.UserId/PhoneVerificationSession.UserId never actually fires (this account is
        // tombstoned, not removed), so both are cleaned up explicitly, in the same transaction. This is
        // what makes the MAX-account identifier (as HMAC) genuinely unrecoverable after deletion (Р4),
        // frees this person's slot in the per-MAX-account ceiling (Q10), and guarantees a future
        // registration on this same number starts unverified (Q9) — by removing the row, not a flag flip.
        db.VerifiedPhones.RemoveRange(
            await db.VerifiedPhones.Where(v => v.UserId == userId
                || (guestMatchPhone != null && v.Phone == guestMatchPhone)).ToListAsync());  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        // Review finding: matching purely by CanonicalPhone (with no UserId check) used to also delete a
        // COMPLETE STRANGER's still-live session on the same number — e.g. someone else mid-registration
        // on the exact phone this account is being deleted from under, whose next poll would 404 without
        // warning. Own sessions (any status) are always this account's to remove; a session that merely
        // SHARES the phone is only swept up once it's already terminal — cleanup, not a race with a live
        // caller.
        db.PhoneVerificationSessions.RemoveRange(
            await db.PhoneVerificationSessions.Where(s => s.UserId == userId
                || (guestMatchPhone != null && s.CanonicalPhone == guestMatchPhone  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                    && s.Status != PhoneVerificationStatus.Pending && s.Status != PhoneVerificationStatus.Linked))
                .ToListAsync());

        // Step 2e (ARCHITECTURE_CYCLE25.md §498.4, §504.4): same reasoning as 2c/2d — the Cascade FKs never fire on a tombstone, so this person's MAX
        // link and link sessions are removed explicitly. Shop notes about the number of THIS account are deleted only when the number is proven
        // (guestMatchPhone, TD-03 gate — an unverified account must not destroy a shop's note about someone else); notes this person AUTHORED stay,
        // with the author's name replaced by "Удалённый пользователь" (no FK on the author, like the acceptance journal).
        db.StaffMaxLinks.RemoveRange(await db.StaffMaxLinks.Where(l => l.UserId == userId).ToListAsync());
        db.StaffMaxLinkSessions.RemoveRange(await db.StaffMaxLinkSessions.Where(s => s.UserId == userId).ToListAsync());
        if (guestMatchPhone != null)
            db.ShopCustomerNotes.RemoveRange(await db.ShopCustomerNotes.Where(n => n.Phone == guestMatchPhone).ToListAsync());  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        foreach (var authored in await db.ShopCustomerNotes.Where(n => n.UpdatedByUserId == userId).ToListAsync())
        {
            authored.UpdatedByUserId = null;
            authored.UpdatedByName = "Удалённый пользователь";
        }

        // Step 3: bookings are anonymized, never deleted — the salon's revenue/commission history for a
        // completed visit must stay intact (US-39 p.3). Matches both the client path and the guest path
        // (a booking made before this person registered, found the same way as step 2's notes).
        var bookingsToAnonymize = await db.Bookings
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone && b.ShowcaseKind == ShowcaseBookingKind.None))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        foreach (var booking in bookingsToAnonymize)
        {
            booking.ClientId = null;
            booking.GuestName = null;
            booking.GuestPhone = null;
            booking.GuestEmail = null;
            booking.Notes = null;
            booking.ClientDeleted = true;
        }

        // Step 3b (ARCHITECTURE_CYCLE23.md §398.2): pickup orders — depersonalized, never deleted: the shop's books (number, lines,
        // totals, status) stay, name/phone/comment/account link go, and the order is marked PersonalDataErased. Same scope as the bookings
        // above: the account's own orders, plus guest orders on a number this account has PROVEN it owns. Active orders are NOT cancelled —
        // the shop still sees "данные покупателя удалены" and the order number and can finish or refuse it.
        var ordersToErase = await db.Orders.Include(o => o.Events)
            .Where(o => o.CustomerUserId == userId || (guestMatchPhone != null && o.CustomerKind == OrderActorKind.Guest && o.CustomerPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — cycle 23, orders follow the same gate as bookings (ARCHITECTURE_CYCLE23.md §398.2)
            .ToListAsync();
        foreach (var order in ordersToErase)
        {
            OrderPersonalData.Erase(order);
            foreach (var orderEvent in order.Events) OrderPersonalData.TombstoneCustomerEvent(orderEvent);
        }
        // ARCHITECTURE_CYCLE24.md §461: the browsers subscribed to those orders are deleted with the personal data (their endpoints identify a device);
        // the messenger choice was reset by Erase, the consent snapshot stays (legal data). The delivery-journal rows of the orders are scrubbed by the
        // notification step below (they carry the account id or the verified phone).
        var erasedOrderIds = ordersToErase.Select(o => o.Id).ToList();
        if (erasedOrderIds.Count > 0)
            db.OrderPushSubscriptions.RemoveRange(await db.OrderPushSubscriptions.Where(s => erasedOrderIds.Contains(s.OrderId)).ToListAsync());
        // "Кто и когда" of a shop's acceptance switch names a person: their own name goes, the fact stays.
        foreach (var settings in await db.ShopSettings.Where(s => s.AcceptanceChangedByUserId == userId).ToListAsync())
        {
            settings.AcceptanceChangedByName = OrderPersonalData.DeletedActorName;
            settings.AcceptanceChangedByUserId = null;
        }

        // TD-05 (ARCHITECTURE_CYCLE16.md §247.2, no migration — §240.3/§247.1). Two rules, both scoped
        // to exactly the set of bookings the gate above already allowed touching (never a separate
        // phone-matching lookup here — that would be a sixth place, §245.2/§247.2):
        //   - Client events for THIS account (ActorUserId == userId) → tombstone.
        //   - Guest events on a booking that was just anonymized above → tombstone (same person, no
        //     account link existed at the time).
        // Staff/SuperAdmin/System events are untouched — different subject, different retention (D1/TD-18).
        const string deletedActorTombstone = "Удалённый пользователь"; // same form as Reviews.ReviewerName above
        var anonymizedBookingIds = bookingsToAnonymize.Select(b => b.Id).ToHashSet();
        var eventsToTombstone = await db.BookingEvents
            .Where(e =>
                (e.ActorKind == BookingActorKind.Client && e.ActorUserId == userId) ||
                (e.ActorKind == BookingActorKind.Guest && anonymizedBookingIds.Contains(e.BookingId)))
            .ToListAsync();
        foreach (var bookingEvent in eventsToTombstone)
            bookingEvent.ActorNameSnapshot = deletedActorTombstone;

        // I9/N9: EVERY notification row for this person carries its own snapshot of the recipient's
        // phone/name/rendered text (§23.4), independent of the booking row anonymized above — cancelling
        // the booking does NOT touch these, and neither does anonymizing only the Pending ones: a
        // terminal row (Sent/Delivered/Failed/...) keeps exactly the same personal data, forever, right
        // next to a booking that's already been scrubbed in the SAME method. Split by whether the row is
        // still actionable:
        //   - Pending: cancelled AND scrubbed (was already sent nowhere — nothing to preserve).
        //   - Everything else (terminal): scrubbed ONLY — Status/Reason/timestamps/ProviderMessageId are
        //     left untouched, since they're the delivery record itself (§23.4: "a permanent journal
        //     entry"), not personal data about the recipient the way the phone/name/body are.
        // NotificationOptOut rows are deliberately NOT touched here — they are what stops the platform
        // from ever messaging this phone again, which is the opposite of what this endpoint should undo.
        var allNotifications = await db.OutboundNotifications
            .Where(n => n.RecipientUserId == userId || (guestMatchPhone != null && n.RecipientPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        foreach (var notification in allNotifications)
        {
            if (notification.Status == NotificationStatus.Pending)
            {
                notification.Status = NotificationStatus.Cancelled;
                notification.Reason = NotificationReason.BookingOrAssignmentCancelled;
            }

            notification.RecipientName = null;
            // N8: empty, not a fake sentinel like the previous "deleted" — PhoneDisplayMask.Mask would
            // otherwise run it through the generic fallback and produce something that LOOKS like a real
            // masked phone ("+de***ed"). Empty never happens on an ordinary row, so
            // CompanyNotificationsController.GetLog's mapping keys off exactly this to show "получатель
            // удалён" instead of masking it.
            notification.RecipientPhone = string.Empty;
            notification.Body = string.Empty;
        }

        // Step 4: reviews are depersonalized, not deleted — the review is about the salon, and the
        // rating/text remain meaningful without the author's identity attached.
        var reviewsToDeperson = await db.Reviews.Where(r => r.ClientId == userId).ToListAsync();
        foreach (var review in reviewsToDeperson)
        {
            review.ClientId = null;
            review.ReviewerName = "Удалённый пользователь";
        }

        // I10 / SPEC US-56 п. 5: this person's own WhatsApp channels (they may own one even without
        // owning a company — gate #2 above only blocks deletion while a COMPANY is still theirs) must be
        // decommissioned too, not left running and billed forever with nobody left who can ever log in
        // to disconnect them. §30.4 database-first only, same as the webhook's ban handling above in this
        // cycle's report (I2/B8): orphan the instance id, blank the channel's own credentials, cancel its
        // Pending rows — the actual provider delete is picked up by ChannelHealthTask's orphan-retry
        // sweep rather than a synchronous provider call inside this already-long transaction.
        var ownedChannels = await db.NotificationChannels
            .Where(c => c.OwnerUserId == userId && c.State != ChannelState.Replaced)
            .ToListAsync();
        foreach (var ownedChannel in ownedChannels)
        {
            var instanceId = ownedChannel.ProviderInstanceId;
            if (instanceId is not null)
            {
                ownedChannel.OrphanedInstanceId = instanceId;
                ownedChannel.ProviderInstanceId = null;
                ownedChannel.ProviderSecretCiphertext = null;
                ownedChannel.ProviderSecretKeyId = null;
            }

            if (ownedChannel.State != ChannelState.DisabledByOwner)
            {
                db.ChannelStateEvents.Add(new ChannelStateEvent
                {
                    Id = Guid.NewGuid(), ChannelId = ownedChannel.Id, FromState = ownedChannel.State,
                    ToState = ChannelState.DisabledByOwner, Reason = ChannelStateReason.DisconnectedByOwner,
                });
                ownedChannel.State = ChannelState.DisabledByOwner;
                ownedChannel.LastStateReason = ChannelStateReason.DisconnectedByOwner;
            }

        }

        // The owned channels' Pending rows are cancelled and their company assignments removed — one
        // query each for ALL owned channels (cycle 22 §375 F15), not two per channel.
        var ownedChannelIds = ownedChannels.Select(c => c.Id).ToList();
        if (ownedChannelIds.Count > 0)
        {
            var channelPending = await db.OutboundNotifications
                .Where(n => n.ChannelId != null && ownedChannelIds.Contains(n.ChannelId.Value) && n.Status == NotificationStatus.Pending)
                .ToListAsync();
            foreach (var row in channelPending)
            {
                row.Status = NotificationStatus.Cancelled;
                row.Reason = NotificationReason.BookingOrAssignmentCancelled;
            }

            // N7: previously left standing — a company (someone ELSE's company, this person only bought
            // the channel) stayed assigned to a channel that will never send again. Its settings screen
            // would keep showing "салон привязан к каналу" for a channel that's now dead, and any booking
            // event there would keep queuing Pending rows that just sit until they expire, instead of the
            // company being told up front there is no usable channel (§23.2's NoUsableChannel gate).
            var channelAssignments = await db.ChannelCompanyAssignments
                .Where(a => ownedChannelIds.Contains(a.ChannelId)).ToListAsync();
            db.ChannelCompanyAssignments.RemoveRange(channelAssignments);
        }

        // Step 5: company memberships are removed and Identity roles resynced — same lock this person's
        // OWN AddMember/RemoveMember calls would have taken, extended here to every company they belong
        // to (US-46, ARCHITECTURE.md §7.4 step 5).
        var membershipCompanyIds = await db.CompanyMembers
            .Where(cm => cm.UserId == userId).Select(cm => cm.CompanyId).Distinct().ToListAsync();
        foreach (var companyId in membershipCompanyIds)
            await AdvisoryLock.AcquireAsync(db, $"company-members:{companyId}");

        var memberships = await db.CompanyMembers.Where(cm => cm.UserId == userId).ToListAsync();
        db.CompanyMembers.RemoveRange(memberships);
        await db.SaveChangesAsync();
        await IdentityRoleSync.SyncAsync(db, userManager, userId);

        // Step 6: the account itself becomes a tombstone. Every personal-data field is scrubbed; login
        // is made impossible two ways at once (PasswordHash cleared AND a permanent lockout), and the
        // phone number is freed for reuse by clearing UserName/PhoneNumber (Identity's unique index is
        // on NormalizedUserName, so "deleted-{id}" being unique is what makes this safe to repeat).
        user.FirstName = "Удалённый";
        user.LastName = "пользователь";
        user.AvatarUrl = null;
        user.PasswordHash = null;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.DeletedAtUtc = DateTime.UtcNow;
        await userManager.SetEmailAsync(user, null);
        await userManager.SetPhoneNumberAsync(user, null);
        await userManager.SetUserNameAsync(user, $"deleted-{user.Id}");
        // Rotates SecurityStamp, which is what actually invalidates every token issued before this
        // moment — Program.cs's OnTokenValidated compares a hash of it on every request (US-39 p.7).
        await userManager.UpdateSecurityStampAsync(user);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Files only after the commit succeeds (ARCHITECTURE.md §1.4) — the worst outcome of a crash
        // between the two is an orphaned file, which photo-retention-cleanup sweeps up later; a DB row
        // pointing at a missing file is the state that must never happen.
        foreach (var (full, thumb) in photoKeysToDelete)
        {
            storage.DeletePrivate(full);
            storage.DeletePrivate(thumb);
        }
        storage.DeletePublic(oldAvatarUrl);

        return new(AccountDeletionStatus.Deleted);
    }
}
