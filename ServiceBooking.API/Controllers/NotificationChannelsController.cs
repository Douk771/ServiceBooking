using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
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
    IChannelProvisioningRegistry provisioningRegistry,
    INotificationTransportRegistry transportRegistry,
    IMemoryCache cache,
    IOptions<NotificationOptions> options,
    BillingAccountProvisioner billingAccountProvisioner,
    PlatformSettings platformSettings,
    LegalDocumentProvider legalProvider,
    ConsentLedger ledger,
    ChannelFundingReader fundingReader,
    PendingRebinder pendingRebinder,
    ILogger<NotificationChannelsController> logger) : ControllerBase
{
    private const string TestMessageText =
        "Проверка канала уведомлений ezbook.ru: если вы видите это сообщение, канал работает.";

    [HttpGet]
    public async Task<ActionResult<ChannelListDto>> GetAll(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // B9: access to a channel you already OWN must never depend on still owning a company — a former
        // owner (company handed to someone else, or simply no companies left) can still see and disconnect
        // a channel they are paying for. Company ownership only gates the "nothing to show yet" path.
        if (!await IsAnyCompanyOwnerAsync(userId) && !await db.NotificationChannels.AnyAsync(c => c.OwnerUserId == userId, ct))
            return Forbid();

        var channels = await db.NotificationChannels.AsNoTracking()
            .Where(c => c.OwnerUserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var idleDays = await PlatformIdleDaysAsync();
        var funding = await fundingReader.LoadAsync(channels, ct);
        var companies = await AccountCompaniesAsync(channels.Select(c => c.BillingAccountId), ct);
        return Ok(new ChannelListDto(channels.Select(c => MapToDto(c, idleDays, funding, CompaniesOf(companies, c))).ToList()));
    }

    [HttpGet("offer")]
    public async Task<ActionResult<ChannelOfferDto>> GetOffer(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        var price = await platformSettings.GetChannelPricePerMonthAsync(ct);

        // B12 (§104.8): riskText/riskVersion now come from the SAME ChannelRiskNotice legal document the
        // /channel-risk public page and accept-risk's own version check read — one noticeholder, not
        // three copies of "roughly the same paragraph" drifting apart.
        var riskDoc = legalProvider.Current?.Get(LegalDocumentType.ChannelRiskNotice);
        if (riskDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        var available = price is not null;
        var transports = new List<TransportOfferDto>
        {
            new(NotificationTransport.WhatsApp, ChannelPresentation.TransportDisplayName(NotificationTransport.WhatsApp),
                available, ChannelPresentation.TransportConnectionNotice(NotificationTransport.WhatsApp)),
            new(NotificationTransport.Max, ChannelPresentation.TransportDisplayName(NotificationTransport.Max),
                available, ChannelPresentation.TransportConnectionNotice(NotificationTransport.Max)),
        };

        return Ok(new ChannelOfferDto(
            PricePerMonth: price, AllowedByPlan: true,
            RiskText: riskDoc.ContentHtml, RiskVersion: riskDoc.Version, Transports: transports));
    }

    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ChannelDto>> Create([FromBody] CreateChannelRequestDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        // T5-B4 (ARCHITECTURE_CYCLE5.md §43.2, API_CONTRACT_CYCLE5.md §50.1, BREAKING № 7): the request
        // body used to be ignored entirely (API_CONTRACT_CYCLE4.md §22) — this cycle requires the legal
        // entity form, a formally-valid ИНН, and acceptance of D9 (the channel offer, an appendix to
        // TermsOwner, §43.2) before a request can even be created. Checked by hand, same reasoning as
        // RegisterDto.Legal.
        if (dto.LegalEntityForm is null || string.IsNullOrWhiteSpace(dto.OfferAccepted?.Version))
            return BadRequest("Укажите форму юридического лица и примите условия оферты.");
        if (!InnValidator.IsValid(dto.Inn))
            return BadRequest("ИНН указан неверно, проверьте цифры");

        var offerDoc = legalProvider.Current?.Get(LegalDocumentType.TermsOwner);
        if (offerDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.OfferAccepted.Version != offerDoc.Version)
            return Conflict("Соглашение владельца было обновлено ещё раз — перечитайте и примите новую редакцию.");

        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);

        // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.4): the tariff flag no longer gates the purchase (no 402 "недоступно на вашем тарифе");
        // availability of the option is the platform switch (§40.7.1), checked by the wizard's POST (BE-40-3).

        // N21, §59/§47.3: `notifications.channel.price-per-month` no longer controls anything — the
        // channel is priced through the `notifications.whatsapp` subscription option now, not this
        // platform setting (which is being removed from the admin screen for the same reason). Gating
        // the request on it being non-null blocked every request the moment nobody had bothered to keep
        // a now-decorative setting non-null, with no way for an owner to tell why.

        // ARCHITECTURE_CYCLE9.md §104.2/§114.2 (US-119): absent → WhatsApp, computed here (not only at
        // BuildRow time below) so the N8 duplicate-transport check right after has a concrete value to
        // compare against.
        var requestedTransport = dto.Transport ?? NotificationTransport.WhatsApp;

        // EnsureAccountAsync is idempotent if the account already exists.
        var ownerAccountId = accountId ?? await billingAccountProvisioner.EnsureAccountAsync(userId);

        // §114.2 (N8): "у аккаунта уже есть канал этого транспорта в живом состоянии" → 409. "Живое"
        // means State != Replaced — the SAME definition LoadFundingAsync already uses to pick the
        // account's working channel per transport (this endpoint's own doc comment on that method).
        // Without this check an owner could request unlimited pending/NotConnected channels of the same
        // transport for one account.
        var hasLiveChannelOfTransport = await db.NotificationChannels.AsNoTracking().AnyAsync(c =>
            c.BillingAccountId == ownerAccountId && c.Transport == requestedTransport && c.State != ChannelState.Replaced);
        if (hasLiveChannelOfTransport)
            return Conflict($"У аккаунта уже есть канал транспорта «{ChannelPresentation.TransportDisplayName(requestedTransport)}» в живом состоянии");

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            BillingAccountId = ownerAccountId,
            // ARCHITECTURE_CYCLE9.md §104.2/§114.2 (US-119): absent → WhatsApp, so a pre-cycle-9 caller
            // that never sends this field keeps requesting exactly what it always requested. An
            // unparseable string in the JSON body never reaches here at all — [ApiController]'s own model
            // binding already 400s a value that doesn't match NotificationTransport before this action runs.
            Transport = requestedTransport,
            State = ChannelState.NotConnected,
            RequestedAtUtc = DateTime.UtcNow,
            LegalEntityForm = dto.LegalEntityForm,
            Inn = dto.Inn,
        };
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync();

        // D9 is an APPENDIX to TermsOwner, not a sixth document type (§43.2) — the same DocumentKey as
        // company creation's acceptance, distinguished only by Purpose. Both rows carry the SAME version:
        // "владелец видел существенные условия платной опции в редакции от такой-то даты" is provable
        // either way.
        await ledger.GrantAsync(new ConsentGrant(
            ConsentSubject.ForUser(userId), LegalDocumentType.TermsOwner.ToString(), offerDoc.Version, offerDoc.ContentHash,
            Purpose: ConsentPurpose.ChannelOffer, ConsentAct.Accepted, ConsentSource.ChannelRequest,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()));

        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var funding = await fundingReader.LoadAsync([channel]);
        var companies = await AccountCompaniesAsync([channel.BillingAccountId], HttpContext.RequestAborted);
        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, MapToDto(channel, idleDays, funding, CompaniesOf(companies, channel)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelDto>> GetById(Guid id, CancellationToken ct)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        var idleDays = await PlatformIdleDaysAsync();
        var funding = await fundingReader.LoadAsync([channel], ct);
        var companies = await AccountCompaniesAsync([channel.BillingAccountId], ct);
        return Ok(MapToDto(channel, idleDays, funding, CompaniesOf(companies, channel)));
    }

    [HttpPost("{id:guid}/accept-risk")]
    public async Task<IActionResult> AcceptRisk(Guid id, [FromBody] AcceptRiskDto dto)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        // B12 (§104.8): compared against the live ChannelRiskNotice document version instead of the
        // deleted NotificationRiskText constant — the SAME version GetOffer's riskVersion just handed
        // the frontend, so "текст изменился, прочитайте заново" is provably about THIS document, not a
        // second copy that could drift from it.
        var riskDoc = legalProvider.Current?.Get(LegalDocumentType.ChannelRiskNotice);
        if (riskDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.Version != riskDoc.Version)
            return BadRequest("Текст изменился, прочитайте заново");

        channel.RiskAcceptedAtUtc = DateTime.UtcNow;
        channel.RiskAcceptedVersion = dto.Version;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/connect")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> Connect(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        var nowUtc = DateTime.UtcNow;
        // §40.3: Connect can only bind a FUNDED number — the first live number of a paid transport (the channel row has no
        // paid period of its own). The tariff flag is gone (§40.3.4): there is no 402 "недоступно на вашем тарифе".
        var funded = await fundingReader.IsFundedAsync(channel);
        var paymentState = funded ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.NotPaid;
        if (!funded)
            return StatusCode(402, "Канал не оплачен");

        if (channel.RiskAcceptedAtUtc is null)
            return Conflict("Сначала подтвердите условия подключения");

        var canConnect = ChannelPresentation.CanConnect(channel.State, paymentState, riskAccepted: true);
        if (!canConnect || channel.ProviderInstanceId is not null)
            return Conflict("Номер уже подключается");

        // T5-B13 (ARCHITECTURE_CYCLE5.md §52.1, API_CONTRACT_CYCLE5.md §50.3): a deliberate stop, not a
        // provider call that would silently create an instance in an unknown region. State is untouched —
        // this is a platform-wide pause, not something specific to this channel.
        if (!options.Value.GreenApi.InstanceCreationEnabled)
            return Conflict("Создание каналов приостановлено платформой");

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
            instance = await provisioningRegistry.For(channel.Transport).CreateInstanceAsync(CancellationToken.None);
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

        // T5-B13 (ARCHITECTURE_CYCLE5.md §52.2): the provider reported a server country that doesn't
        // match what we expected — the instance is never wired up (no ProviderInstanceId/secret
        // persisted). This is a PLATFORM-side incident, not a transient failure: the channel goes to
        // Disconnected (not back to NotConnected, and not left in Connecting) so ChannelPresentation
        // shows "требуется вмешательство платформы" and CanConnect keeps the retry button hidden — an
        // owner clicking Connect again would just reproduce the same mismatch.
        if (instance.ServerCountry is { Length: > 0 } reportedCountry &&
            !string.Equals(reportedCountry, options.Value.GreenApi.ServerCountry, StringComparison.OrdinalIgnoreCase))
        {
            // Only the country values and the channel id — never the token, the full response body, or
            // the request URL (ARCHITECTURE_CYCLE4.md §24.3's three rungs, unchanged by this cycle).
            logger.LogError(
                "GREEN-API server country mismatch for channel {ChannelId}: expected {ExpectedCountry}, provider reported {ReportedCountry}",
                channel.Id, options.Value.GreenApi.ServerCountry, reportedCountry);

            // Code review В7: the instance already exists at the provider (created, billed) by the
            // CreateInstanceAsync call above — discarding `instance.InstanceId` here, as an earlier
            // version did, would leak it: nothing would ever reference it again, so it would go on
            // living, billing, and sitting in the wrong jurisdiction with no way to shut it down. §30.4's
            // established "database first" orphan pattern applies exactly here, the same as
            // ChannelHealthTask.DeleteInstanceAsync uses for every other forced decommission: record
            // OrphanedInstanceId now (so the id itself, and the fact that it needs cleanup, survive this
            // request even if the process crashes right after), and let ChannelHealthTask's existing
            // orphan-retry sweep (RetryOrphanDeletionForAsync) delete it at the provider on its next
            // pass — reusing tested infrastructure instead of a second, ad hoc deletion call here that
            // would have no retry if it failed.
            channel.State = ChannelState.Disconnected;
            channel.LastStateReason = ChannelStateReason.ServerCountryMismatch;
            channel.InstanceCreatedAtUtc = null;
            channel.OrphanedInstanceId = instance.InstanceId;
            db.ChannelStateEvents.Add(new ChannelStateEvent
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id,
                FromState = ChannelState.Connecting, ToState = ChannelState.Disconnected,
                Reason = ChannelStateReason.ServerCountryMismatch,
                Detail = $"expected={options.Value.GreenApi.ServerCountry} reported={reportedCountry} orphanedInstanceId={instance.InstanceId}",
            });
            await db.SaveChangesAsync();
            return StatusCode(503, "Требуется вмешательство платформы для восстановления канала.");
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
        // ARCHITECTURE_CYCLE9.md §104.7: the ORIGINAL provider-webhook/{token} route stays WhatsApp-only
        // (it may already be configured at a live instance) — a WhatsApp channel keeps using it exactly
        // as before this cycle. A MAX channel is configured against the NEW provider-webhook/{transport}/
        // {token} route instead, so its delivery-status/state events reach GreenApiMaxWebhookParser (via
        // the transport-keyed registry) rather than the WhatsApp-only parser at the old route — the two
        // parsers agree on field NAMES but not on which NotificationReason a "noAccount"/"failed" status
        // means, so a MAX channel pointed at the wrong route would log the wrong reason for every failure.
        var webhookUrl = string.IsNullOrEmpty(options.Value.WebhookToken)
            ? null
            : channel.Transport == NotificationTransport.WhatsApp
                ? $"https://{NotificationTemplateValidator.OwnDomain}/api/notifications/provider-webhook/{options.Value.WebhookToken}"
                : $"https://{NotificationTemplateValidator.OwnDomain}/api/notifications/provider-webhook/{channel.Transport}/{options.Value.WebhookToken}";
        try
        {
            await provisioningRegistry.For(channel.Transport).ConfigureInstanceAsync(
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

            snapshot = await provisioningRegistry.For(channel.Transport).GetQrAsync(credentials, HttpContext.RequestAborted);
            cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(2));
        }

        if (snapshot.Authorized)
        {
            // The provider only learns which number scanned the QR at this moment, so this is the one
            // place the channel can find out its own number; null means the lookup failed, not "no number".
            if (snapshot.PhoneNumber is not null)
                channel.PhoneNumber = snapshot.PhoneNumber;
            // Shared with NotificationsController's webhook path and the scheduled tasks (§336 risk
            // A1) so the trial mailing-window-start hook lives in exactly one place.
            await ChannelStateTransition.Apply(
                db, channel, ChannelState.Connected, ChannelStateReason.Authorized, null, DateTime.UtcNow);
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
        var outcome = await transportRegistry.For(channel.Transport).SendAsync(credentials, ownerPhone, TestMessageText, HttpContext.RequestAborted);
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
    /// paid period belongs to the billing account (cycle 22, §379) and stays with it; company assignments
    /// stay with the account; the old one becomes terminal
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
            // §47.1's stability guarantee is about ranking, not about the row itself losing its
            // account — a replaced-after-ban number still belongs to the SAME account it always did.
            BillingAccountId = channel.BillingAccountId,
            // ARCHITECTURE_CYCLE9.md §104.3: a replacement is a same-transport swap — copied explicitly
            // rather than left at NotificationChannel.Transport's own WhatsApp default, or a banned MAX
            // channel would silently "replace" into a WhatsApp one, and every moved assignment below
            // would then fail the composite FK (assignment.Transport still says Max, newChannel.Transport
            // would say WhatsApp).
            Transport = channel.Transport,
            State = ChannelState.NotConnected,
            RequestedAtUtc = DateTime.UtcNow,
        };
        db.NotificationChannels.Add(newChannel);

        // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.4, §40.9): the number serves every company of the account, so there are no company
        // assignments to move. The Pending queue rows follow to the new number of the SAME transport (§27: "Expired не воскрешаются" —
        // only Pending rows are ever touched).
        await pendingRebinder.OnReplaceAsync(channel, newChannel);

        channel.ReplacedByChannelId = newChannel.Id;
        channel.State = ChannelState.Replaced;
        channel.LastStateReason = ChannelStateReason.ReplacedAfterBan;
        // N6 / cycle 22 (§379, Р2): nothing to move or clear for the paid period — it belongs to the
        // billing account (its WhatsApp option / subscription), not to the channel row. The terminal row
        // is never funded (ChannelFunding.Rank skips Replaced), so nothing counts it twice.
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id,
            FromState = ChannelState.Blocked, ToState = ChannelState.Replaced, Reason = ChannelStateReason.ReplacedAfterBan,
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // The account's paid period as the owner's list shows it for the new channel (ChannelFundingReader).
        var funding = await fundingReader.LoadAsync([newChannel]);
        // companiesMoved = the companies of the account (§40.28.3): all of them are served by the new number.
        var companiesServed = channel.BillingAccountId is { } replacedAccountId
            ? await db.Companies.CountAsync(c => c.BillingAccountId == replacedAccountId) : 0;
        return StatusCode(201, new ReplaceChannelResponseDto(newChannel.Id, funding.GetValueOrDefault(newChannel.Id)?.PaidUntil, companiesServed));
    }

    /// <summary>ARCHITECTURE_CYCLE40.md §40.28.5 — legacy route: the number serves every company of the account, so there is nothing to
    /// assign. 404 for a foreign channel, otherwise 410 with a plain Russian line; nothing is read from the body or changed.</summary>
    [HttpPost("{id:guid}/companies")]
    public async Task<IActionResult> AssignCompany(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();
        return new ContentResult
        {
            StatusCode = StatusCodes.Status410Gone,
            Content = "Назначать компании больше не нужно: номер работает для всех ваших компаний",
            ContentType = "text/plain; charset=utf-8",
        };
    }

    /// <summary>§40.28.5 — legacy route, kept idempotent: 404 for a foreign channel, otherwise 204; no data changes (the old assignment
    /// rows are no longer read and the queue is not touched).</summary>
    [HttpDelete("{id:guid}/companies/{companyId:guid}")]
    public async Task<IActionResult> UnassignCompany(Guid id, Guid companyId)
    {
        var channel = await LoadOwnedChannelAsync(id);
        return channel is null ? NotFound() : NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>§40.4: the companies a number serves = all companies of its billing account.</summary>
    private async Task<ILookup<Guid, ChannelCompanyDto>> AccountCompaniesAsync(IEnumerable<Guid?> accountIds, CancellationToken ct)
    {
        var ids = accountIds.Where(a => a.HasValue).Select(a => a!.Value).Distinct().ToList();
        if (ids.Count == 0) return Enumerable.Empty<ChannelCompanyDto>().ToLookup(_ => Guid.Empty);
        var rows = await db.Companies.AsNoTracking().Where(c => c.BillingAccountId != null && ids.Contains(c.BillingAccountId.Value))
            .OrderBy(c => c.CreatedAt).Select(c => new { AccountId = c.BillingAccountId!.Value, Dto = new ChannelCompanyDto(c.Id, c.Name, c.IsActive) }).ToListAsync(ct);
        return rows.ToLookup(r => r.AccountId, r => r.Dto);
    }

    private static IReadOnlyList<ChannelCompanyDto> CompaniesOf(ILookup<Guid, ChannelCompanyDto> lookup, NotificationChannel channel) =>
        channel.BillingAccountId is { } accountId ? lookup[accountId].ToList() : [];

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
        // Cycle 40 (§40.9, Р40-Ю3): the Pending rows are cancelled; moved to the other messenger only when the configuration flag says so.
        await pendingRebinder.OnUnbindAsync(channel);

        await db.SaveChangesAsync();

        if (credentials is not null)
        {
            try
            {
                await provisioningRegistry.For(channel.Transport).LogoutAsync(credentials, HttpContext.RequestAborted);
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
            deletion = await provisioningRegistry.For(channel.Transport).DeleteInstanceAsync(instanceId, HttpContext.RequestAborted);
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
        var query = db.NotificationChannels.AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();

        var channel = await query.FirstOrDefaultAsync(c => c.Id == id);
        return channel is not null && channel.OwnerUserId == userId ? channel : null;
    }

    private Task<int> PlatformIdleDaysAsync() => platformSettings.GetChannelIdleDaysAsync();

    // §47.3: PaymentState/PaidUntil keep their FORM but their SOURCE is `funding` (the account's
    // subscription-driven ranking). Cycle 22 (§379/§380, Р2/Р6): the channel's own PaidFromUtc/PaidUntilUtc
    // columns are dropped, and so is the always-null PaidFrom field.
    private static ChannelDto MapToDto(
        NotificationChannel channel, int idleDays, IReadOnlyDictionary<Guid, ChannelFundingInfo> funding,
        IReadOnlyList<ChannelCompanyDto> companies)
    {
        var (fundingState, fundingText, subscriptionPaidUntil) = funding.TryGetValue(channel.Id, out var f)
            ? f
            : new ChannelFundingInfo(ChannelFundingState.NotPaid, MessengerTexts.FundingText(ChannelFundingState.NotPaid, channel.Transport, new TransportPaymentView(null, false), null), null);
        var paymentState = fundingState switch
        {
            ChannelFundingState.Funded => ChannelPaymentStatus.Paid,
            _ when channel.IsSuspendedByAdmin => ChannelPaymentStatus.Suspended,
            _ => ChannelPaymentStatus.NotPaid,
        };
        var phoneMasked = channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber);
        // N6, §47.3: StateText/ChannelDto.paidUntil both read the funding's paid-until (the WhatsApp
        // option's, else the subscription's) — the channel row has no paid period of its own.
        var stateText = ChannelPresentation.StateText(channel.State, phoneMasked, idleDays, subscriptionPaidUntil, channel.LastStateReason);
        var riskAccepted = channel.RiskAcceptedAtUtc is not null;

        return new ChannelDto(
            channel.Id, channel.Transport, channel.State, stateText, phoneMasked, paymentState,
            subscriptionPaidUntil, channel.RequestedAtUtc, channel.ConnectedAtUtc,
            channel.RiskAcceptedAtUtc, channel.IdleSinceUtc,
            channel.IdleSinceUtc is not null ? channel.IdleSinceUtc.Value.AddDays(idleDays) : null,
            channel.ReplacedByChannelId,
            companies,
            CanConnect: ChannelPresentation.CanConnect(channel.State, paymentState, riskAccepted),
            CanReplace: ChannelPresentation.CanReplace(channel.State),
            FundingState: fundingState,
            FundingText: fundingText,
            Inn: channel.Inn, LegalEntityForm: channel.LegalEntityForm);
    }
}
