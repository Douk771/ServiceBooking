using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Everything that happens once a MAX update has passed webhook-token authentication (ARCHITECTURE_
/// CYCLE12.md §145, §146, §147) — session lookup, the two mandatory checks (§147.1/§147.2), vCard
/// parsing (§147.3), the per-account ceiling (§147.5), and the actual status/VerifiedPhone write. Called
/// from <c>PhoneVerificationController.ReceiveWebhook</c>, which is what guarantees the caller's own
/// contract (§146.2's "always 200, exceptions logged, never propagated").
/// </summary>
public sealed class MaxWebhookHandler(
    AppDbContext db,
    IOptions<PhoneVerificationOptions> options,
    IMaxBotClient botClient,
    PhoneVerificationWriter writer,
    ILogger<MaxWebhookHandler> logger)
{
    public async Task HandleAsync(JsonElement root, CancellationToken ct)
    {
        var update = MaxUpdateParser.Parse(root);

        switch (update.UpdateType)
        {
            case "bot_started":
                await HandleBotStartedAsync(update, ct);
                break;
            case "message_created" when update.Contact is not null:
                await HandleContactAsync(update, ct);
                break;
            default:
                // §146.2: an update this subsystem doesn't recognize is logged, never rejected — a
                // non-2xx response here is what actually costs the webhook subscription (О4).
                logger.LogInformation("phone-verification webhook: unrecognized update (type={UpdateType})", update.UpdateType);
                break;
        }
    }

    private async Task HandleBotStartedAsync(MaxUpdateData update, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(update.Payload) || string.IsNullOrEmpty(update.SenderId))
        {
            logger.LogInformation("phone-verification webhook: bot_started missing payload or sender id");
            return;
        }

        var payloadHash = PayloadGenerator.Hash(update.Payload);
        var session = await db.PhoneVerificationSessions.FirstOrDefaultAsync(s => s.PayloadHash == payloadHash, ct);
        if (session is null)
        {
            logger.LogInformation("phone-verification webhook: bot_started for an unknown payload");
            await ReplyAsync(update.ChatId, MaxBotTexts.PayloadExpiredOrUnknown, ct);
            return;
        }

        var externalAccountKey = ComputeExternalAccountKey(update.SenderId);
        var expired = PhoneVerificationStateMachine.IsExpired(session.Status, session.ExpiresAtUtc, DateTime.UtcNow);
        var decision = PhoneVerificationStateMachine.EvaluateBotStarted(
            session.Status, expired, session.ExternalAccountKey, externalAccountKey);

        string replyText;
        if (decision.Outcome == PhoneVerificationStateMachine.StepOutcome.Applied)
        {
            session.Status = PhoneVerificationStatus.Linked;
            session.ExternalAccountKey = externalAccountKey;
            session.LinkedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            replyText = MaxBotTexts.Greeting;
        }
        else if (decision.Outcome == PhoneVerificationStateMachine.StepOutcome.NoOp)
        {
            replyText = MaxBotTexts.AlreadyLinkedReminder;
        }
        else
        {
            replyText = decision.Reason == PhoneVerificationFailureReason.PayloadLinkedToAnotherAccount
                ? MaxBotTexts.PayloadLinkedToAnotherAccount
                : MaxBotTexts.PayloadExpiredOrUnknown;
        }

        await ReplyAsync(update.ChatId, replyText, ct);
    }

    /// <summary>
    /// §147's check order (signature → ownership → vCard has a usable number → matches the session →
    /// §147.5 ceiling), §145.2's "запись отметки под advisory-локом в одной транзакции".
    ///
    /// Session correlation note: a <c>contact</c> update carries no payload of its own (only
    /// <c>bot_started</c> does) — this handler correlates it to the MOST RECENTLY linked, still-Linked
    /// session for this MAX account (one MAX↔bot conversation, so the newest open link is the one the
    /// person is actively responding to). This reading of the platform's protocol is unconfirmed against
    /// a live bot (same caveat as §147.1's own note) — worth validating during QA's functional pass.
    /// </summary>
    private async Task HandleContactAsync(MaxUpdateData update, CancellationToken ct)
    {
        if (update.Contact is null || string.IsNullOrEmpty(update.SenderId)) return;

        var externalAccountKey = ComputeExternalAccountKey(update.SenderId);
        var now = DateTime.UtcNow;

        var session = await db.PhoneVerificationSessions
            .Where(s => s.ExternalAccountKey == externalAccountKey && s.Status == PhoneVerificationStatus.Linked)
            .OrderByDescending(s => s.LinkedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (session is null)
        {
            logger.LogInformation("phone-verification webhook: contact received with no matching linked session");
            return;
        }

        var expired = PhoneVerificationStateMachine.IsExpired(session.Status, session.ExpiresAtUtc, now);

        // §147.1/§147.2 — both mandatory and independent.
        var signatureValid = MaxContactSignature.Verify(update.Contact.VcfInfo, update.Contact.Hash, options.Value.Max.BotToken ?? string.Empty);
        var contactOwnedBySender = !string.IsNullOrEmpty(update.Contact.OwnerId) && update.Contact.OwnerId == update.SenderId;

        // §147.3.
        var phones = MaxVCardParser.ExtractPhones(update.Contact.VcfInfo, options.Value.Max.MaxVcardBytes);
        var hasUsablePhone = phones.Count > 0;
        var phoneMatches = phones.Contains(session.CanonicalPhone);
        var earlyChecksPass = !expired && signatureValid && contactOwnedBySender && hasUsablePhone && phoneMatches;

        // §145.2: attempt count, the ceiling check, and the terminal write all happen in ONE transaction
        // — the advisory lock only needs to be held for the part that could actually write VerifiedPhone.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        session.AttemptCount++;

        var limitReached = false;
        if (earlyChecksPass)
        {
            await AdvisoryLock.AcquireAsync(db, $"phone-verification:{externalAccountKey}");
            var otherPhonesCount = await db.VerifiedPhones.CountAsync(
                v => v.ExternalAccountKey == externalAccountKey && v.Phone != session.CanonicalPhone, ct);
            limitReached = otherPhonesCount >= options.Value.MaxPhonesPerExternalAccount;
        }

        var checks = new PhoneVerificationStateMachine.ContactChecks(signatureValid, contactOwnedBySender, hasUsablePhone, phoneMatches, limitReached);
        var decision = PhoneVerificationStateMachine.EvaluateContact(session.Status, expired, checks);

        string? replyText = null;
        switch (decision.Outcome)
        {
            case PhoneVerificationStateMachine.StepOutcome.Applied:
                session.Status = PhoneVerificationStatus.Verified;
                session.CompletedAtUtc = now;
                session.ConsumableUntilUtc = now.AddMinutes(Math.Max(1, options.Value.VerifiedSessionUsableMinutes));

                if (session.Purpose == PhoneVerificationPurpose.Profile)
                {
                    var user = session.UserId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
                    var writeOutcome = await writer.WriteAsync(session, user, options.Value.MaxPhonesPerExternalAccount, ct);
                    if (writeOutcome == PhoneVerificationWriter.WriteOutcome.LimitReached)
                    {
                        // Defence in depth: the pre-check above already reserved the slot under the same
                        // lock, so this branch should be unreachable in practice — WriteAsync re-checks
                        // for real regardless of what the pre-check found.
                        session.Status = PhoneVerificationStatus.Rejected;
                        session.FailureReason = PhoneVerificationFailureReason.MaxAccountLimitReached;
                        replyText = MaxBotTexts.MaxAccountLimitReached;
                        break;
                    }

                    // §148.3: for Profile, there is nobody left to "present" this session to — it is
                    // consumed the instant the contact is accepted.
                    session.Status = PhoneVerificationStatus.Consumed;
                }

                replyText = MaxBotTexts.Success;
                break;

            case PhoneVerificationStateMachine.StepOutcome.Rejected:
                session.Status = PhoneVerificationStatus.Rejected;
                session.FailureReason = decision.Reason;
                session.CompletedAtUtc = now;
                if (decision.Reason == PhoneVerificationFailureReason.PhoneMismatch && phones.Count > 0)
                    session.MismatchedPhoneMasked = PhoneDisplayMask.Mask(phones[0]);
                replyText = ReplyTextFor(decision.Reason, phones, session.CanonicalPhone);
                break;

            case PhoneVerificationStateMachine.StepOutcome.NoOp:
                break; // §145.2: redelivery of an already-terminal session — nothing else to do
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (replyText is not null)
            await ReplyAsync(update.ChatId, replyText, ct);
    }

    private static string ReplyTextFor(PhoneVerificationFailureReason? reason, IReadOnlyList<string> sentPhones, string canonicalPhone) => reason switch
    {
        PhoneVerificationFailureReason.SignatureMismatch => MaxBotTexts.SignatureMismatch,
        PhoneVerificationFailureReason.ContactNotOwnedBySender => MaxBotTexts.ContactNotOwnedBySender,
        PhoneVerificationFailureReason.NoPhoneInContact => MaxBotTexts.NoPhoneInContact,
        PhoneVerificationFailureReason.PhoneMismatch => MaxBotTexts.PhoneMismatch(
            PhoneDisplayMask.Mask(sentPhones.Count > 0 ? sentPhones[0] : string.Empty), PhoneDisplayMask.Mask(canonicalPhone)),
        PhoneVerificationFailureReason.MaxAccountLimitReached => MaxBotTexts.MaxAccountLimitReached,
        _ => MaxBotTexts.PayloadExpiredOrUnknown,
    };

    private string ComputeExternalAccountKey(string maxUserId) =>
        ExternalAccountKey.Compute(options.Value.ExternalKeyHmac ?? string.Empty, maxUserId);

    /// <summary>§146.2's own timeout budget — a reply that never arrives must not hold the webhook
    /// response open, and a failure here must never surface as anything but a log line (the caller has
    /// already committed the session's own state by the time this runs).</summary>
    private async Task ReplyAsync(string? chatId, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(chatId)) return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.Value.Max.TimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await botClient.SendMessageAsync(chatId, text, linkedCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "phone-verification webhook: failed to send a reply to the bot chat");
        }
    }
}
