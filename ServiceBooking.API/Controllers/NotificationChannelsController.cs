using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// A platform owner's WhatsApp channels: list, request, QR binding, risk acceptance, test message,
/// unbinding, and company assignment (API_CONTRACT_CYCLE4.md §20–§26, T4-B4/T4-B6). SuperAdmin has NO
/// access here at all (§19.3) — the admin-facing surface is a separate, read-mostly view under
/// <c>AdminController</c>.
/// </summary>
[ApiController]
[Route("api/notification-channels")]
[Authorize]
public class NotificationChannelsController(
    AppDbContext db,
    IChannelProvisioning provisioning,
    INotificationTransport transport,
    IMemoryCache cache,
    IOptions<NotificationOptions> options,
    SubscriptionResolver subscriptionResolver,
    PlatformSettings platformSettings,
    ILogger<NotificationChannelsController> logger) : ControllerBase
{
    private const string TestMessageText =
        "Проверка канала уведомлений ezbook.ru: если вы видите это сообщение, канал работает.";

    [HttpGet]
    public async Task<ActionResult<ChannelListDto>> GetAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // B9: access to a channel you already OWN must never depend on still owning a company — a former
        // owner (company handed to someone else, or simply no companies left) can still see and disconnect
        // a channel they are paying for. Company ownership only gates the "nothing to show yet" path.
        if (!await IsAnyCompanyOwnerAsync(userId) && !await db.NotificationChannels.AnyAsync(c => c.OwnerUserId == userId))
            return Forbid();

        var channels = await db.NotificationChannels.AsNoTracking()
            .Include(c => c.Assignments).ThenInclude(a => a.Company)
            .Where(c => c.OwnerUserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var idleDays = await PlatformIdleDaysAsync();
        return Ok(new ChannelListDto(channels.Select(c => MapToDto(c, idleDays)).ToList()));
    }

    [HttpGet("offer")]
    public async Task<ActionResult<ChannelOfferDto>> GetOffer()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        var plan = accountId.HasValue
            ? await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId.Value)
            : EffectivePlan.Free;
        var price = await platformSettings.GetChannelPricePerMonthAsync();
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();

        return Ok(new ChannelOfferDto(
            Available: price is not null, PricePerMonth: price, Currency: "RUB",
            IdleDays: idleDays, PlanAllows: plan.AllowNotificationChannel,
            RiskTextVersion: NotificationRiskText.CurrentVersion));
    }

    [HttpPost]
    public async Task<ActionResult<ChannelDto>> Create()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        // The request body is intentionally ignored (API_CONTRACT_CYCLE4.md §22: "тела нет... сервер
        // его игнорирует и не валидирует" — email collection on this form was cut by the customer, §31.3
        // of the architecture). The contract also still shows a stale `{ "contactEmail": ... }` example
        // left over from an earlier draft; this developer flagged the inconsistency rather than silently
        // picking one reading — see the cover note in the cycle report.

        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        var plan = accountId.HasValue
            ? await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId.Value)
            : EffectivePlan.Free;
        if (!plan.AllowNotificationChannel)
            return StatusCode(402, "Подключение канала недоступно на вашем тарифе");

        var price = await platformSettings.GetChannelPricePerMonthAsync();
        if (price is null)
            return Conflict("Подключение каналов временно недоступно");

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            State = ChannelState.NotConnected,
            RequestedAtUtc = DateTime.UtcNow,
        };
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync();

        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, MapToDto(channel, idleDays));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelDto>> GetById(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        var idleDays = await PlatformIdleDaysAsync();
        return Ok(MapToDto(channel, idleDays));
    }

    [HttpPost("{id:guid}/accept-risk")]
    public async Task<IActionResult> AcceptRisk(Guid id, [FromBody] AcceptRiskDto dto)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (dto.Version != NotificationRiskText.CurrentVersion)
            return BadRequest("Текст изменился, прочитайте заново");

        channel.RiskAcceptedAtUtc = DateTime.UtcNow;
        channel.RiskAcceptedVersion = dto.Version;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/connect")]
    public async Task<IActionResult> Connect(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        // Reviewer note / SPEC US-31 п. 7: the plan could have downgraded since the channel was
        // purchased (channel and AccountSubscription are billed independently, §33) — Connect must not
        // let a since-downgraded owner keep reconnecting a channel their current plan no longer allows.
        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        var plan = accountId.HasValue
            ? await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId.Value)
            : EffectivePlan.Free;
        if (!plan.AllowNotificationChannel)
            return StatusCode(402, "Подключение канала недоступно на вашем тарифе");

        var nowUtc = DateTime.UtcNow;
        var paymentState = ChannelPaymentState.Of(channel, nowUtc);
        if (paymentState != ChannelPaymentStatus.Paid)
            return StatusCode(402, "Канал не оплачен");

        if (channel.RiskAcceptedAtUtc is null)
            return Conflict("Сначала подтвердите условия подключения");

        var canConnect = ChannelPresentation.CanConnect(channel.State, paymentState, riskAccepted: true);
        if (!canConnect || channel.ProviderInstanceId is not null)
            return Conflict("Номер уже подключается");

        var encryptionKey = options.Value.EncryptionKey;
        if (string.IsNullOrEmpty(encryptionKey))
        {
            // Defensive only — DeploymentSafetyChecks.ValidateNotificationSecrets already refuses to
            // start the app with a real provider configured and no key outside Development (I4: gated on
            // Provider, not the removed Notifications:Enabled), so this branch is reachable only in a
            // dev/test process on the "logging" provider, where Connect shouldn't realistically be
            // exercised in the first place.
            logger.LogError("Notifications:EncryptionKey is not configured; cannot store the provider secret for channel {ChannelId}", channel.Id);
            return StatusCode(503, "Сервис подключения временно недоступен, попробуйте позже");
        }

        // I3: a double-click (or a retried request racing the original) must not create two paid
        // instances — the SECOND SaveChanges below used to be the only guard, and it just silently
        // overwrote the first request's ProviderInstanceId, orphaning that instance forever (nothing
        // left pointing at it to ever clean it up). Fixed by recording intent — State flips to
        // Connecting — BEFORE the network call, inside its own short transaction serialized by an
        // advisory lock keyed on this channel: the second request blocks until the first commits, then
        // re-reads State as Connecting and is rejected by the SAME canConnect/409 check above, instead of
        // racing it.
        await using (var lockTransaction = await db.Database.BeginTransactionAsync())
        {
            await AdvisoryLock.AcquireAsync(db, $"channel-connect:{id}");

            var currentState = await db.NotificationChannels.Where(c => c.Id == id)
                .Select(c => new { c.State, c.ProviderInstanceId }).FirstAsync();
            if (!ChannelPresentation.CanConnect(currentState.State, paymentState, riskAccepted: true) || currentState.ProviderInstanceId is not null)
            {
                await lockTransaction.RollbackAsync();
                return Conflict("Номер уже подключается");
            }

            channel.State = ChannelState.Connecting;
            channel.InstanceCreatedAtUtc = nowUtc;
            await db.SaveChangesAsync();
            await lockTransaction.CommitAsync();
        }

        ProvisionedInstance instance;
        try
        {
            // N4: deliberately NOT HttpContext.RequestAborted — an owner closing the tab must not race
            // (and possibly win against) the provider having already created a BILLED instance. If the
            // browser cancels, this call keeps running to completion server-side regardless (nothing
            // downstream is awaiting RequestAborted either); the only remaining boundary is
            // HttpClient.Timeout (§28.1's "green-api" named client), same as every other necessary-but-
            // irreversible provider call in this cycle.
            instance = await provisioning.CreateInstanceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create a provider instance for channel {ChannelId}", channel.Id);
            // Revert the intent marker — otherwise the channel is stuck in Connecting with no instance
            // to ever clean up (ChannelHealthTask's stuck-in-Connecting timeout only acts on a channel
            // that HAS a ProviderInstanceId) and the owner can never retry.
            channel.State = ChannelState.NotConnected;
            channel.InstanceCreatedAtUtc = null;
            await db.SaveChangesAsync();
            return StatusCode(503, "Сервис подключения временно недоступен, попробуйте позже");
        }

        channel.ProviderInstanceId = instance.InstanceId;
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt(instance.Token, encryptionKey, channel.Id);
        channel.ProviderSecretKeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(encryptionKey));
        await db.SaveChangesAsync();

        // N5: ONE setSettings call for the antiban send delay (§29.1 step 2) AND the webhook (I2, §32) —
        // GREEN-API restarts the instance on every settings change, so two separate calls meant the owner
        // watched it restart twice, back-to-back, right before the QR code was shown. Best effort as a
        // whole: a failure here must not undo a binding that already succeeded at the provider. webhookUrl
        // is left null (webhook fields omitted from the body entirely) when no webhook token is configured
        // (dev/test with the logging provider, or Production before T4-D2 wires the .env variable) rather
        // than registering a callback URL nobody can authenticate against. Deliberately NOT
        // HttpContext.RequestAborted, same reasoning as CreateInstanceAsync above — this call still
        // mutates the SAME billed instance, and a closed tab must not race it either.
        var webhookUrl = string.IsNullOrEmpty(options.Value.WebhookToken)
            ? null
            : $"https://{NotificationTemplateValidator.OwnDomain}/api/notifications/provider-webhook/{options.Value.WebhookToken}";
        try
        {
            await provisioning.ConfigureInstanceAsync(
                new ChannelCredentials(instance.InstanceId, instance.Token), options.Value.Dispatch.PauseMinMs,
                webhookUrl, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ConfigureInstance (send delay/webhook) failed for channel {ChannelId} (non-critical)", channel.Id);
        }

        return Accepted(new ConnectResponseDto(ChannelState.Connecting, RefreshAfterSeconds: 3));
    }

    [HttpGet("{id:guid}/qr")]
    public async Task<ActionResult<QrResponseDto>> GetQr(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (channel.State != ChannelState.Connecting && channel.State != ChannelState.Connected)
            return Conflict("Канал не в процессе подключения");

        if (channel.State == ChannelState.Connected)
            return Ok(new QrResponseDto(ChannelState.Connected, null, RefreshAfterSeconds: 3, ExpiresInSeconds: 0));

        if (channel.ProviderInstanceId is null || channel.ProviderSecretCiphertext is null)
            return Conflict("Канал не в процессе подключения");

        var cacheKey = $"channel-qr:{channel.Id}";
        if (!cache.TryGetValue(cacheKey, out QrSnapshot? snapshot) || snapshot is null)
        {
            ChannelCredentials credentials;
            try
            {
                credentials = DecryptCredentials(channel);
            }
            catch (ChannelSecretUnavailableException ex)
            {
                logger.LogWarning(ex, "Channel {ChannelId} secret unavailable while polling QR", channel.Id);
                return Conflict("Канал не в процессе подключения");
            }

            snapshot = await provisioning.GetQrAsync(credentials, HttpContext.RequestAborted);
            cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(2));
        }

        if (snapshot.Authorized)
        {
            channel.State = ChannelState.Connected;
            channel.LastStateReason = ChannelStateReason.Authorized;
            channel.ConnectedAtUtc = DateTime.UtcNow;
            channel.ConsecutiveSendFailures = 0; // I8: a fresh QR reconnect must not inherit the old disconnect-threshold count
            // The provider only learns which number scanned the QR at this moment, so this is the one
            // place the channel can find out its own number; null means the lookup failed, not "no number".
            if (snapshot.PhoneNumber is not null)
                channel.PhoneNumber = snapshot.PhoneNumber;
            db.ChannelStateEvents.Add(new ChannelStateEvent
            {
                Id = Guid.NewGuid(),
                ChannelId = channel.Id,
                FromState = ChannelState.Connecting,
                ToState = ChannelState.Connected,
                Reason = ChannelStateReason.Authorized,
            });
            await db.SaveChangesAsync();
            cache.Remove(cacheKey);

            return Ok(new QrResponseDto(ChannelState.Connected, null, snapshot.RefreshAfterSeconds, ExpiresInSeconds: 0));
        }

        return Ok(new QrResponseDto(ChannelState.Connecting, snapshot.Base64Png, snapshot.RefreshAfterSeconds, ExpiresInSeconds: 20));
    }

    [HttpPost("{id:guid}/test-message")]
    public async Task<ActionResult<TestMessageResponseDto>> SendTestMessage(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (channel.State != ChannelState.Connected)
            return Conflict("Канал не подключён");

        var cooldown = TimeSpan.FromMinutes(options.Value.TestMessageCooldownMinutes);
        if (channel.LastTestMessageAtUtc is { } lastAt && DateTime.UtcNow - lastAt < cooldown)
            return StatusCode(429, "Проверять канал можно не чаще одного раза в 5 минут");

        var ownerPhone = await db.Users.AsNoTracking()
            .Where(u => u.Id == channel.OwnerUserId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(ownerPhone))
            return Conflict("У вашего аккаунта не указан номер телефона");

        ChannelCredentials credentials;
        try
        {
            credentials = DecryptCredentials(channel);
        }
        catch (ChannelSecretUnavailableException)
        {
            return Conflict("Канал не подключён");
        }

        channel.LastTestMessageAtUtc = DateTime.UtcNow;
        var outcome = await transport.SendAsync(credentials, ownerPhone, TestMessageText, HttpContext.RequestAborted);
        await db.SaveChangesAsync();

        return outcome switch
        {
            SendOutcome.Sent => Ok(new TestMessageResponseDto(true, "Сообщение отправлено на ваш номер")),
            _ => Ok(new TestMessageResponseDto(false, "Не удалось отправить сообщение, попробуйте позже")),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        // I10: pending-row cancellation now happens INSIDE DecommissionInstanceAsync's own DB-first
        // SaveChanges (§30.4 step 1) — previously a second, separate SaveChanges after it returned,
        // which meant a crash between the two left the channel decommissioned but its queue still full
        // of Pending rows for a channel that will never send them.
        await DecommissionInstanceAsync(channel, ChannelState.DisabledByOwner, ChannelStateReason.DisconnectedByOwner);

        return NoContent();
    }

    /// <summary>B8 / API_CONTRACT_CYCLE4.md §27, US-63 — replacing a banned number. No re-payment: the
    /// paid period and company assignments move to a fresh channel row; the old one becomes terminal
    /// (<see cref="ChannelState.Replaced"/>) with a pointer forward. The owner then goes through the
    /// ordinary accept-risk/connect/QR flow (§24) on the new channel — this endpoint only does the move.</summary>
    [HttpPost("{id:guid}/replace")]
    public async Task<ActionResult<ReplaceChannelResponseDto>> Replace(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (!ChannelPresentation.CanReplace(channel.State))
            return Conflict("Канал не заблокирован");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"channel-assignment:{id}");

        var newChannel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            State = ChannelState.NotConnected,
            RequestedAtUtc = DateTime.UtcNow,
            PaidFromUtc = channel.PaidFromUtc,
            PaidUntilUtc = channel.PaidUntilUtc,
        };
        db.NotificationChannels.Add(newChannel);

        // Company assignments move wholesale — the unique index on CompanyId means these rows are
        // updated in place, not deleted+recreated, so AssignedByUserId/history on the assignment itself
        // survives the swap.
        var assignments = await db.ChannelCompanyAssignments.Where(a => a.ChannelId == id).ToListAsync();
        foreach (var assignment in assignments)
            assignment.ChannelId = newChannel.Id;

        // Pending queue rows re-bind to the new channel (§27: "Expired не воскрешаются" — this WHERE only
        // ever touches Pending, so an already-Expired/Failed/Sent row is untouched by construction).
        var pending = await db.OutboundNotifications
            .Where(n => n.ChannelId == id && n.Status == NotificationStatus.Pending)
            .ToListAsync();
        foreach (var row in pending)
            row.ChannelId = newChannel.Id;

        channel.ReplacedByChannelId = newChannel.Id;
        channel.State = ChannelState.Replaced;
        channel.LastStateReason = ChannelStateReason.ReplacedAfterBan;
        // N6: the paid period already moved to newChannel (captured above) — left set here, this
        // terminal row would still read as ChannelPaymentState.Of(...) == Paid, and an admin summary
        // that flags "expires within 7 days" would count the SAME paid period twice, once per channel.
        channel.PaidFromUtc = null;
        channel.PaidUntilUtc = null;
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id,
            FromState = ChannelState.Blocked, ToState = ChannelState.Replaced, Reason = ChannelStateReason.ReplacedAfterBan,
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return StatusCode(201, new ReplaceChannelResponseDto(newChannel.Id, newChannel.PaidUntilUtc, assignments.Count));
    }

    [HttpPost("{id:guid}/companies")]
    public async Task<ActionResult<ChannelDto>> AssignCompany(Guid id, [FromBody] AssignCompanyDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        var company = await db.Companies.FindAsync(dto.CompanyId);
        if (company is null || company.OwnerUserId != userId) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"channel-assignment:{id}");

        var existingAssignment = await db.ChannelCompanyAssignments
            .FirstOrDefaultAsync(a => a.CompanyId == dto.CompanyId);

        if (existingAssignment is not null)
        {
            if (existingAssignment.ChannelId == id)
            {
                // Idempotent re-assignment of the same company to the same channel — no-op 201.
                var idleDaysSame = await PlatformIdleDaysAsync();
                await transaction.CommitAsync();
                return await BuildAssignedResponseAsync(channel, idleDaysSame);
            }
            return Conflict("Салон уже привязан к другому номеру");
        }

        var otherCompanyCount = await db.ChannelCompanyAssignments.CountAsync(a => a.ChannelId == id);
        if (otherCompanyCount > 0 && !dto.WarningAcknowledged)
            return Conflict("Требуется подтверждение: несколько салонов на одном номере");

        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(),
            ChannelId = id,
            CompanyId = dto.CompanyId,
            AssignedByUserId = userId,
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var idleDays = await PlatformIdleDaysAsync();
        return await BuildAssignedResponseAsync(channel, idleDays);
    }

    [HttpDelete("{id:guid}/companies/{companyId:guid}")]
    public async Task<IActionResult> UnassignCompany(Guid id, Guid companyId)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        var assignment = await db.ChannelCompanyAssignments
            .FirstOrDefaultAsync(a => a.ChannelId == id && a.CompanyId == companyId);
        if (assignment is null) return NoContent(); // already gone — DELETE is idempotent

        db.ChannelCompanyAssignments.Remove(assignment);

        var pending = await db.OutboundNotifications
            .Where(n => n.ChannelId == id && n.CompanyId == companyId && n.Status == NotificationStatus.Pending)
            .ToListAsync();
        foreach (var row in pending)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<ActionResult<ChannelDto>> BuildAssignedResponseAsync(NotificationChannel channel, int idleDays)
    {
        await db.Entry(channel).Collection(c => c.Assignments).LoadAsync();
        foreach (var assignment in channel.Assignments)
            await db.Entry(assignment).Reference(a => a.Company).LoadAsync();
        return StatusCode(201, MapToDto(channel, idleDays));
    }

    private async Task DecommissionInstanceAsync(NotificationChannel channel, ChannelState targetState, ChannelStateReason reason)
    {
        // ARCHITECTURE_CYCLE4.md §30.4: database first, then provider, retry from the database. An
        // instance is only actually deleted here when one exists — a channel that never got past
        // NotConnected/Connecting-without-an-instance has nothing to decommission at the provider.
        var instanceId = channel.ProviderInstanceId;
        var fromState = channel.State;

        // I10: best-effort logout with the CHANNEL's own (still valid at the provider) credentials,
        // captured BEFORE they're blanked below — same reasoning and same swallow-independently-of-the-
        // delete pattern as ChannelHealthTask.DeleteInstanceAsync. A decrypt failure here must never
        // block the decommission itself.
        ChannelCredentials? credentials = null;
        if (instanceId is not null && channel.ProviderSecretCiphertext is { } ciphertextForLogout)
        {
            try
            {
                var encryptionKey = options.Value.EncryptionKey ?? string.Empty;
                var token = SecretProtector.Decrypt(ciphertextForLogout, encryptionKey, channel.Id);
                credentials = new ChannelCredentials(instanceId, token);
            }
            catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
            {
                logger.LogDebug(ex, "Could not decrypt channel secret for a best-effort logout before decommission, skipping logout: channelId={ChannelId}", channel.Id);
            }
        }

        channel.OrphanedInstanceId = instanceId;
        channel.ProviderInstanceId = null;
        channel.ProviderSecretCiphertext = null;
        channel.ProviderSecretKeyId = null;
        channel.State = targetState;
        channel.LastStateReason = reason;
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, FromState = fromState, ToState = targetState, Reason = reason,
        });

        // I10: cancelling Pending rows moved INTO this DB-first step (was a second, separate SaveChanges
        // after this method returned) — one transaction, not two, so a crash in between can't leave a
        // decommissioned channel with a queue still full of rows it will never send.
        var pending = await db.OutboundNotifications
            .Where(n => n.ChannelId == channel.Id && n.Status == NotificationStatus.Pending)
            .ToListAsync();
        foreach (var row in pending)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }

        await db.SaveChangesAsync();

        if (credentials is not null)
        {
            try
            {
                await provisioning.LogoutAsync(credentials, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Best-effort logout before instance deletion failed, continuing: channelId={ChannelId}", channel.Id);
            }
        }

        if (instanceId is null) return;

        InstanceDeletion deletion;
        try
        {
            deletion = await provisioning.DeleteInstanceAsync(instanceId, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Left as OrphanedInstanceId for ChannelHealthTask to retry (§30.4) — not this developer's
            // background task, but the field it drains is shared schema, so failing loudly here (rather
            // than swallowing silently) keeps the failure visible without duplicating the retry logic.
            logger.LogError(ex, "Failed to delete provider instance {InstanceId} for channel {ChannelId}; left orphaned for retry",
                instanceId, channel.Id);
            return;
        }

        // B6: DeleteInstanceAsync can fail WITHOUT throwing (InstanceDeletion.Success == false) — that
        // result was previously ignored, so a failed deletion got logged and billed as if it had
        // succeeded, and OrphanedInstanceId (the only thing that makes ChannelHealthTask retry it) was
        // cleared with nothing left to retry.
        if (!deletion.Success)
        {
            logger.LogError("Provider instance deletion did not confirm success for {InstanceId} on channel {ChannelId}; left orphaned for retry",
                instanceId, channel.Id);
            return;
        }

        channel.OrphanedInstanceId = null;
        await db.SaveChangesAsync();
        logger.LogWarning("Deleted provider instance {InstanceId} for channel {ChannelId} ({Reason})", instanceId, channel.Id, reason);
    }

    private ChannelCredentials DecryptCredentials(NotificationChannel channel)
    {
        if (channel.ProviderInstanceId is null || channel.ProviderSecretCiphertext is null)
            throw new ChannelSecretUnavailableException("Channel has no stored provider instance.");

        var encryptionKey = options.Value.EncryptionKey ?? throw new ChannelSecretUnavailableException("Encryption key not configured.");
        var token = SecretProtector.Decrypt(channel.ProviderSecretCiphertext, encryptionKey, channel.Id);
        return new ChannelCredentials(channel.ProviderInstanceId, token);
    }

    private async Task<bool> IsAnyCompanyOwnerAsync(string userId) =>
        await db.CompanyMembers.AnyAsync(cm => cm.UserId == userId && cm.Role == UserRole.CompanyOwner);

    /// <summary>Channel ownership mismatch is 404, not 403 (API_CONTRACT_CYCLE4.md §19.2: "чужой канал
    /// неотличим от несуществующего") — unlike company ownership in <see cref="AssignCompany"/>, which
    /// the contract calls out as an explicit 403 (§25.1).</summary>
    private async Task<NotificationChannel?> LoadOwnedChannelAsync(Guid id, bool forUpdate = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.NotificationChannels.Include(c => c.Assignments).ThenInclude(a => a.Company).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();

        var channel = await query.FirstOrDefaultAsync(c => c.Id == id);
        return channel is not null && channel.OwnerUserId == userId ? channel : null;
    }

    private Task<int> PlatformIdleDaysAsync() => platformSettings.GetChannelIdleDaysAsync();

    private static ChannelDto MapToDto(NotificationChannel channel, int idleDays)
    {
        var nowUtc = DateTime.UtcNow;
        var paymentState = ChannelPaymentState.Of(channel, nowUtc);
        var phoneMasked = channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber);
        var stateText = ChannelPresentation.StateText(channel.State, phoneMasked, idleDays, channel.PaidUntilUtc, channel.LastStateReason);
        var riskAccepted = channel.RiskAcceptedAtUtc is not null;

        return new ChannelDto(
            channel.Id, channel.State, stateText, phoneMasked, paymentState,
            channel.PaidFromUtc, channel.PaidUntilUtc, channel.RequestedAtUtc, channel.ConnectedAtUtc,
            channel.RiskAcceptedAtUtc, channel.IdleSinceUtc,
            channel.IdleSinceUtc is not null ? channel.IdleSinceUtc.Value.AddDays(idleDays) : null,
            channel.ReplacedByChannelId,
            channel.Assignments.Select(a => new ChannelCompanyDto(a.CompanyId, a.Company.Name, a.Company.IsActive)).ToList(),
            CanConnect: ChannelPresentation.CanConnect(channel.State, paymentState, riskAccepted),
            CanReplace: ChannelPresentation.CanReplace(channel.State));
    }
}
