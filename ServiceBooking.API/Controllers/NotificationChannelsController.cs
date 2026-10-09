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
/// A platform owner's messenger numbers: the wizard's overview, list, payment request / terms, QR binding, risk acceptance, test message,
/// unbinding and replacement (API_CONTRACT_CYCLE4.md §20–§26; reworked by API_CONTRACT_CYCLE40.md §40.23–§40.28). SuperAdmin has NO
/// access here at all (§19.3) — the admin-facing surface is a separate, read-mostly view under
/// <c>AdminController</c>. Cycle 40: one number serves every company of the account, so the company-assignment routes only answer
/// 410 / 204 and nothing here reads the assignments.
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
    AccountMessagingReader messagingReader,
    NumbersOverviewBuilder overviewBuilder,
    ILogger<NotificationChannelsController> logger) : ControllerBase
{
    private const string DocumentsUnavailableText = "Правовые документы временно недоступны.";

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

        var ctx = await overviewBuilder.LoadAsync(channels.Select(c => c.BillingAccountId ?? Guid.Empty), userId, ct);
        return Ok(new ChannelListDto(channels.Select(c => NumbersOverviewBuilder.BuildChannel(c, ctx)).ToList()));
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.23 — everything the "Numbers" block and its wizard need in one answer.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<NumbersOverviewDto>> GetOverview(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId) && !await db.NotificationChannels.AnyAsync(c => c.OwnerUserId == userId, ct))
            return Forbid();

        var overview = await overviewBuilder.BuildAsync(userId, ct);
        return overview is null ? StatusCode(StatusCodes.Status503ServiceUnavailable, DocumentsUnavailableText) : Ok(overview);
    }

    [HttpGet("offer")]
    public async Task<ActionResult<ChannelOfferDto>> GetOffer(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        // B12 (§104.8): riskText/riskVersion now come from the SAME ChannelRiskNotice legal document the
        // /channel-risk public page and accept-risk's own version check read — one noticeholder, not
        // three copies of "roughly the same paragraph" drifting apart.
        var riskDoc = legalProvider.Current?.Get(LegalDocumentType.ChannelRiskNotice);
        if (riskDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, DocumentsUnavailableText);

        // Cycle 40 (§40.28.2): only the transports that can be sold are offered; the price of the old top-level field is WhatsApp's.
        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        var account = await messagingReader.ForAccountAsync(accountId ?? Guid.Empty, ct: ct);
        var transports = new List<TransportOfferDto>();
        decimal? whatsAppPrice = null;
        foreach (var t in account!.Transports.Where(t => t.Option.Sellable))
        {
            var price = t.Option.PricePerMonth;
            if (t.Transport == NotificationTransport.WhatsApp) whatsAppPrice = price;
            transports.Add(new TransportOfferDto(
                t.Transport, ChannelPresentation.TransportDisplayName(t.Transport), account.PlatformEnabled,
                ChannelPresentation.TransportConnectionNotice(t.Transport), price, price is { } p ? MessengerTexts.PriceText(p) : null));
        }

        return Ok(new ChannelOfferDto(
            PricePerMonth: whatsAppPrice, AllowedByPlan: true,
            RiskText: riskDoc.ContentHtml, RiskVersion: riskDoc.Version, Transports: transports));
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.24 — the "Payment" step (<c>paymentRequest: true</c>) and the "Terms" step
    /// (<c>paymentRequest: false</c>) of the wizard. The order of the refusals is the contract's order.</summary>
    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ChannelDto>> Create([FromBody] CreateChannelRequestDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await IsAnyCompanyOwnerAsync(userId)) return Forbid();

        // T5-B4: form, a formally-valid ИНН and acceptance of D9 (the channel offer, an appendix to TermsOwner, §43.2) are checked by hand.
        if (dto.LegalEntityForm is null || string.IsNullOrWhiteSpace(dto.OfferAccepted?.Version))
            return BadRequest("Укажите форму юридического лица и примите условия оферты.");
        if (!InnValidator.IsValid(dto.Inn))
            return BadRequest("ИНН указан неверно, проверьте цифры");

        var offerDoc = legalProvider.Current?.Get(LegalDocumentType.TermsOwner);
        var riskDoc = legalProvider.Current?.Get(LegalDocumentType.ChannelRiskNotice);
        if (offerDoc is null || (dto.RiskAccepted is not null && riskDoc is null))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, DocumentsUnavailableText);
        if (dto.OfferAccepted.Version != offerDoc.Version)
            return Conflict("Соглашение владельца было обновлено ещё раз — перечитайте и примите новую редакцию.");
        if (dto.RiskAccepted is not null && dto.RiskAccepted.Version != riskDoc!.Version)
            return BadRequest(MessengerTexts.RiskTextChanged);

        // ARCHITECTURE_CYCLE9.md §104.2: absent → WhatsApp.
        var requestedTransport = dto.Transport ?? NotificationTransport.WhatsApp;
        var display = MessengerTexts.DisplayName(requestedTransport);

        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, userId);
        var account = (await messagingReader.ForAccountAsync(accountId ?? Guid.Empty, ct: ct))!;
        if (!account.PlatformEnabled)
            return Conflict("Подключение временно недоступно");
        var transport = account.For(requestedTransport);

        if (dto.PaymentRequest)
        {
            if (!transport.Option.Sellable) return Conflict(MessengerTexts.Unavailable(requestedTransport));
            if (transport.Primary is not null && transport.Paid && !transport.Payment.IsTrial)
                return Conflict(MessengerTexts.AlreadyHaveNumber(requestedTransport));
        }
        else if (!transport.Paid)
        {
            return Conflict(MessengerTexts.PayFirst(requestedTransport));
        }

        // EnsureAccountAsync is idempotent if the account already exists.
        var ownerAccountId = accountId ?? await billingAccountProvisioner.EnsureAccountAsync(userId);
        var now = DateTime.UtcNow;
        NotificationChannel channel;
        bool created;

        // One request at a time per account and transport: two clicks must not create two rows of one number.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await AdvisoryLock.AcquireAsync(db, $"channel-request:{ownerAccountId}:{requestedTransport}");

            var live = await db.NotificationChannels
                .Where(c => c.BillingAccountId == ownerAccountId && c.Transport == requestedTransport && c.State != ChannelState.Replaced)
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (dto.PaymentRequest && live is not null && transport.Paid && !transport.Payment.IsTrial)
                return Conflict(MessengerTexts.AlreadyHaveNumber(requestedTransport));

            created = live is null;
            channel = live ?? new NotificationChannel
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                BillingAccountId = ownerAccountId,
                Transport = requestedTransport,
                State = ChannelState.NotConnected,
            };
            channel.LegalEntityForm = dto.LegalEntityForm;
            channel.Inn = dto.Inn;
            if (dto.RiskAccepted is not null)
            {
                channel.RiskAcceptedAtUtc = now;
                channel.RiskAcceptedVersion = dto.RiskAccepted.Version;
            }
            // The payment request (re)starts "Оплата на проверке"; the Terms step never touches it (Т40-L-03).
            if (dto.PaymentRequest) channel.RequestedAtUtc = now;
            if (created) db.NotificationChannels.Add(channel);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // D9 is an APPENDIX to TermsOwner, not a sixth document type (§43.2) — the same DocumentKey as company creation's
        // acceptance, distinguished only by Purpose.
        await ledger.GrantAsync(new ConsentGrant(
            ConsentSubject.ForUser(userId), LegalDocumentType.TermsOwner.ToString(), offerDoc.Version, offerDoc.ContentHash,
            Purpose: ConsentPurpose.ChannelOffer, ConsentAct.Accepted, ConsentSource.ChannelRequest,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()), ct);

        var result = await overviewBuilder.BuildChannelAsync(channel, userId, ct);
        logger.LogInformation("Number {Transport} {Step} for channel {ChannelId} ({Outcome})",
            display, dto.PaymentRequest ? "payment request" : "terms", channel.Id, created ? "created" : "updated");
        return created ? CreatedAtAction(nameof(GetById), new { id = channel.Id }, result) : Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelDto>> GetById(Guid id, CancellationToken ct)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        return Ok(await overviewBuilder.BuildChannelAsync(channel, channel.OwnerUserId, ct));
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

    /// <summary>API_CONTRACT_CYCLE40.md §40.25 — the start of the wizard's "QR" step. The order of the refusals is the contract's order.</summary>
    [HttpPost("{id:guid}/connect")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> Connect(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (!await platformSettings.IsCustomerMessagingEnabledAsync(ct))
            return Conflict(MessengerTexts.PlatformDisabled);
        if (channel.IsSuspendedByAdmin)
            return Conflict("Номер приостановлен администратором");

        // §40.3: Connect can only bind a FUNDED number — the first live number of a paid transport. Whether the option is still
        // open for sale is not asked here: what was bought (or granted by the trial) keeps working.
        if (!await fundingReader.IsFundedAsync(channel, ct))
            return StatusCode(402, "Номер не оплачен");

        // Т40-L-03: the terms of the CURRENT edition (risk version, status and ИНН, offer) — a new edition sends every number through the Terms step.
        if (!await overviewBuilder.IsTermsAcceptedAsync(channel, userId, ct))
            return Conflict(MessengerTexts.TermsChangedConflict);

        var refusal = ConnectRefusal(channel);
        if (refusal is not null) return refusal;
        if (channel.State == ChannelState.Connecting)
            return Accepted(new ConnectResponseDto(ChannelState.Connecting, RefreshAfterSeconds: 3));

        // T5-B13 (ARCHITECTURE_CYCLE5.md §52.1): a deliberate stop, not a provider call that would silently create an instance in an
        // unknown region. State is untouched — this is a platform-wide pause, not something specific to this channel.
        if (!options.Value.GreenApi.InstanceCreationEnabled)
            return Conflict("Создание каналов приостановлено платформой");

        var encryptionKey = options.Value.EncryptionKey;
        if (string.IsNullOrEmpty(encryptionKey))
        {
            // Defensive only — DeploymentSafetyChecks.ValidateNotificationSecrets already refuses to start the app with a real provider
            // configured and no key outside Development.
            logger.LogError("Notifications:EncryptionKey is not configured; cannot store the provider secret for channel {ChannelId}", channel.Id);
            return StatusCode(503, "Сервис подключения временно недоступен, попробуйте позже");
        }

        // I3: a double-click (or a retried request racing the original) must not create two paid instances — recording intent (State flips
        // to Connecting) happens BEFORE the network call, inside its own short transaction serialized by an advisory lock keyed on this
        // channel: the second request blocks until the first commits, then re-reads State as Connecting and is answered 202 without a new instance.
        var sourceState = channel.State;
        var sourceReason = channel.LastStateReason;
        string? oldInstanceId;
        ChannelCredentials? oldCredentials = null;
        await using (var lockTransaction = await db.Database.BeginTransactionAsync(ct))
        {
            await AdvisoryLock.AcquireAsync(db, $"channel-connect:{id}");
            await db.Entry(channel).ReloadAsync(ct);

            refusal = ConnectRefusal(channel);
            if (refusal is not null)
            {
                await lockTransaction.RollbackAsync(ct);
                return refusal;
            }
            if (channel.State == ChannelState.Connecting)
            {
                await lockTransaction.RollbackAsync(ct);
                return Accepted(new ConnectResponseDto(ChannelState.Connecting, RefreshAfterSeconds: 3));
            }

            sourceState = channel.State;
            sourceReason = channel.LastStateReason;
            oldInstanceId = channel.ProviderInstanceId;
            if (oldInstanceId is not null) oldCredentials = TryDecryptCredentials(channel);

            // A rebinding (Disconnected, or any state that still holds an instance): the old instance is queued for deletion
            // (database first, §30.4) and its secret dropped; the new instance starts from a clean slate.
            string? strandedOrphan = null;
            if (oldInstanceId is not null)
            {
                if (channel.OrphanedInstanceId is { } earlier && earlier != oldInstanceId) strandedOrphan = earlier;
                channel.OrphanedInstanceId = oldInstanceId;
                channel.ProviderInstanceId = null;
                channel.ProviderSecretCiphertext = null;
                channel.ProviderSecretKeyId = null;
                db.ChannelStateEvents.Add(new ChannelStateEvent
                {
                    Id = Guid.NewGuid(), ChannelId = channel.Id, FromState = channel.State, ToState = ChannelState.Connecting,
                    Reason = ChannelStateReason.RebindStarted,
                });
            }

            channel.State = ChannelState.Connecting;
            channel.InstanceCreatedAtUtc = DateTime.UtcNow;
            // Found defect (§40.7.3 item 4): ConnectedAtUtc of the previous binding must not survive into the new one.
            channel.ConnectedAtUtc = null;
            await db.SaveChangesAsync(ct);
            await lockTransaction.CommitAsync(ct);

            if (strandedOrphan is not null)
                await TryDeleteInstanceAsync(channel.Transport, strandedOrphan);
        }

        ProvisionedInstance instance;
        try
        {
            // N4: deliberately NOT HttpContext.RequestAborted — an owner closing the tab must not race (and possibly win against) the
            // provider having already created a BILLED instance.
            instance = await provisioningRegistry.For(channel.Transport).CreateInstanceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create a provider instance for channel {ChannelId}", channel.Id);
            // Revert the intent marker — otherwise the channel is stuck in Connecting with no instance and the owner can never retry.
            channel.State = sourceState == ChannelState.Connecting ? ChannelState.NotConnected : sourceState;
            channel.LastStateReason = sourceReason;
            channel.InstanceCreatedAtUtc = null;
            await db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(503, "Сервис подключения временно недоступен, попробуйте позже");
        }

        // The old instance (if any) is already queued as OrphanedInstanceId; delete it now, the channel-health sweep retries on failure.
        if (oldInstanceId is not null)
            await DeleteOldInstanceAsync(channel, oldInstanceId, oldCredentials);

        // Cycle 40 (§40.7.4): what the provider reported is recorded on the channel (null when it reported nothing) and compared with the
        // country expected FOR THIS TRANSPORT.
        var expectedCountry = options.Value.GreenApi.ExpectedServerCountry(channel.Transport);
        channel.ProviderServerCountry = instance.ServerCountry is { Length: > 0 } reported ? reported : null;

        // T5-B13 (§52.2): a platform-side incident, not a transient failure: the instance is never wired up, the channel goes to Disconnected
        // (retry hidden), and the created instance is queued for deletion (code review В7, §30.4 "database first").
        if (instance.ServerCountry is { Length: > 0 } reportedCountry &&
            !string.Equals(reportedCountry, expectedCountry, StringComparison.OrdinalIgnoreCase))
        {
            // Only the country values and the channel id — never the token, the full response body, or the request URL.
            logger.LogError(
                "GREEN-API server country mismatch for channel {ChannelId} ({Transport}): expected {ExpectedCountry}, provider reported {ReportedCountry}",
                channel.Id, channel.Transport, expectedCountry, reportedCountry);

            // An older orphan (the previous binding's instance whose deletion did not confirm) must not be overwritten by the new one.
            if (channel.OrphanedInstanceId is { } olderOrphan && olderOrphan != instance.InstanceId)
                await TryDeleteInstanceAsync(channel.Transport, olderOrphan);

            channel.State = ChannelState.Disconnected;
            channel.LastStateReason = ChannelStateReason.ServerCountryMismatch;
            channel.InstanceCreatedAtUtc = null;
            channel.OrphanedInstanceId = instance.InstanceId;
            db.ChannelStateEvents.Add(new ChannelStateEvent
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id,
                FromState = ChannelState.Connecting, ToState = ChannelState.Disconnected,
                Reason = ChannelStateReason.ServerCountryMismatch,
                Detail = $"expected={expectedCountry} reported={reportedCountry} orphanedInstanceId={instance.InstanceId}",
            });
            await db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(503, "Требуется вмешательство платформы для восстановления канала");
        }

        channel.ProviderInstanceId = instance.InstanceId;
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt(instance.Token, encryptionKey, channel.Id);
        channel.ProviderSecretKeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(encryptionKey));
        await db.SaveChangesAsync(CancellationToken.None);

        // N5: ONE setSettings call for the antiban send delay (§29.1 step 2) AND the webhook (I2, §32). Best effort as a whole: a failure here
        // must not undo a binding that already succeeded at the provider. Deliberately NOT HttpContext.RequestAborted (same reasoning as above).
        // ARCHITECTURE_CYCLE9.md §104.7: the ORIGINAL provider-webhook/{token} route stays WhatsApp-only; MAX uses provider-webhook/{transport}/{token}.
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

    /// <summary>§40.25 item 7 (and the states that cannot start a binding): the refusal for the channel's CURRENT state, or <see langword="null"/>
    /// when it may proceed (<c>Connecting</c> is answered 202 by the caller, the four source states start a binding).</summary>
    private ObjectResult? ConnectRefusal(NotificationChannel channel) => channel.State switch
    {
        ChannelState.Connected => Conflict("Номер уже подключён"),
        ChannelState.Blocked => Conflict("Номер заблокирован — замените его"),
        ChannelState.Replaced => Conflict("Номер заменён"),
        ChannelState.Disconnected when channel.LastStateReason == ChannelStateReason.ServerCountryMismatch =>
            Conflict("Требуется вмешательство платформы для восстановления канала"),
        _ => null,
    };

    [HttpGet("{id:guid}/qr")]
    public async Task<ActionResult<QrResponseDto>> GetQr(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (channel.State != ChannelState.Connecting && channel.State != ChannelState.Connected)
            return Conflict("Канал не в процессе подключения");

        if (channel.State == ChannelState.Connected)
            return Ok(new QrResponseDto(ChannelState.Connected, null, RefreshAfterSeconds: 3, ExpiresInSeconds: 0));

        // §40.26: between "Connecting" being recorded and the instance being created there is nothing to poll yet — not an error.
        if (channel.ProviderInstanceId is null)
            return Ok(new QrResponseDto(ChannelState.Connecting, null, RefreshAfterSeconds: 2, ExpiresInSeconds: 0));
        if (channel.ProviderSecretCiphertext is null)
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
            // Two tabs polling the same QR must not both write the transition: the move to Connected happens under the channel's connect lock
            // and is written once (the automatic check message is marked inside ChannelStateTransition.Apply, once per instance).
            await using var transaction = await db.Database.BeginTransactionAsync();
            await AdvisoryLock.AcquireAsync(db, $"channel-connect:{id}");
            await db.Entry(channel).ReloadAsync();
            if (channel.State == ChannelState.Connecting)
            {
                // The provider only learns which number scanned the QR at this moment, so this is the one place the channel can find out its
                // own number; null means the lookup failed, not "no number".
                if (snapshot.PhoneNumber is not null)
                    channel.PhoneNumber = snapshot.PhoneNumber;
                await ChannelStateTransition.Apply(
                    db, channel, ChannelState.Connected, ChannelStateReason.Authorized, null, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            cache.Remove(cacheKey);

            return Ok(new QrResponseDto(ChannelState.Connected, null, snapshot.RefreshAfterSeconds, ExpiresInSeconds: 0));
        }

        return Ok(new QrResponseDto(ChannelState.Connecting, snapshot.Base64Png, snapshot.RefreshAfterSeconds, ExpiresInSeconds: 20));
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.27 — the manual check message. The result also lands in <c>lastTest</c> and the channel's journal.</summary>
    [HttpPost("{id:guid}/test-message")]
    public async Task<ActionResult<TestMessageResponseDto>> SendTestMessage(Guid id, CancellationToken ct)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (!await platformSettings.IsCustomerMessagingEnabledAsync(ct))
            return Conflict(MessengerTexts.PlatformDisabled);

        if (channel.State != ChannelState.Connected)
            return Conflict("Канал не подключён");

        var cooldown = TimeSpan.FromMinutes(options.Value.TestMessageCooldownMinutes);
        if (channel.LastTestMessageAtUtc is { } lastAt && DateTime.UtcNow - lastAt < cooldown)
            return StatusCode(429, "Проверять канал можно не чаще одного раза в 5 минут");

        var ownerPhone = await db.Users.AsNoTracking()
            .Where(u => u.Id == channel.OwnerUserId).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
        if (!PhoneNormalizer.TryNormalize(ownerPhone, out var recipient))
            return Conflict("У вашего аккаунта не указан номер телефона");

        if (ChannelTestMessageRule.Decide(true, false, ownerPhone, channel.PhoneNumber, options.Value.TestMessage.AllowSameNumber)
            == ChannelTestDecision.SkipSameNumber)
            return Conflict("Номер канала совпадает с телефоном вашего аккаунта — проверочное сообщение на него не отправляем");

        ChannelCredentials credentials;
        try
        {
            credentials = DecryptCredentials(channel);
        }
        catch (ChannelSecretUnavailableException)
        {
            return Conflict("Канал не подключён");
        }

        var now = DateTime.UtcNow;
        channel.LastTestMessageAtUtc = now;
        var outcome = await transportRegistry.For(channel.Transport)
            .SendAsync(credentials, recipient, MessengerTexts.TestMessageBody(channel.Transport), HttpContext.RequestAborted);
        var delivered = outcome is SendOutcome.Sent;
        channel.LastTestResult = delivered ? ChannelTestResult.Sent : ChannelTestResult.Failed;
        channel.LastTestResultAtUtc = now;
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, FromState = channel.State, ToState = channel.State,
            Reason = delivered ? ChannelStateReason.TestMessageSent : ChannelStateReason.TestMessageFailed,
            Detail = delivered ? null : ChannelTestFailure.SendFailed, OccurredAtUtc = now,
        });
        await db.SaveChangesAsync();

        return delivered
            ? Ok(new TestMessageResponseDto(true, "Сообщение отправлено на ваш номер"))
            : Ok(new TestMessageResponseDto(false, "Не удалось отправить сообщение, попробуйте позже"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        // I10: pending-row cancellation happens INSIDE the DB-first step's own SaveChanges (§30.4 step 1).
        await DecommissionInstanceAsync(channel, ChannelState.DisabledByOwner, ChannelStateReason.DisconnectedByOwner);

        return NoContent();
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.28.3 — replacing a number (a banned one, or any bound one the owner wants to change). No re-payment: the
    /// paid period belongs to the transport of the billing account and stays with it. The new row copies the form, ИНН, risk and the payment
    /// request; waiting messages follow the number to the new row; the old row becomes terminal (<see cref="ChannelState.Replaced"/>) with a
    /// pointer forward and its provider instance is released. The owner then goes through the Terms/QR steps for the new number.</summary>
    [HttpPost("{id:guid}/replace")]
    public async Task<ActionResult<ReplaceChannelResponseDto>> Replace(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        if (channel.State == ChannelState.Replaced) return Conflict("Номер уже заменён");
        if (channel.State == ChannelState.NotConnected) return Conflict("Заменить можно только привязанный номер");

        NotificationChannel newChannel;
        DecommissionPlan plan;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await AdvisoryLock.AcquireAsync(db, $"channel-replace:{id}");
            await db.Entry(channel).ReloadAsync(ct);
            if (channel.State == ChannelState.Replaced) return Conflict("Номер уже заменён");
            if (channel.State == ChannelState.NotConnected) return Conflict("Заменить можно только привязанный номер");

            newChannel = new NotificationChannel
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                // §47.1's stability guarantee is about ranking, not about the row itself losing its account.
                BillingAccountId = channel.BillingAccountId,
                // ARCHITECTURE_CYCLE9.md §104.3: a replacement is a same-transport swap.
                Transport = channel.Transport,
                State = ChannelState.NotConnected,
                // Form, ИНН, risk and the payment request come along: the owner's declaration and the open request do not start over.
                RequestedAtUtc = channel.RequestedAtUtc,
                LegalEntityForm = channel.LegalEntityForm,
                Inn = channel.Inn,
                RiskAcceptedAtUtc = channel.RiskAcceptedAtUtc,
                RiskAcceptedVersion = channel.RiskAcceptedVersion,
            };
            db.NotificationChannels.Add(newChannel);

            // Waiting messages follow the number to the new row of the same transport (§27: only Pending rows move; Expired/Failed/Sent stay).
            var pending = await db.OutboundNotifications
                .Where(n => n.ChannelId == id && n.Status == NotificationStatus.Pending)
                .ToListAsync(ct);
            foreach (var row in pending)
                row.ChannelId = newChannel.Id;

            channel.ReplacedByChannelId = newChannel.Id;
            var reason = channel.State == ChannelState.Blocked ? ChannelStateReason.ReplacedAfterBan : ChannelStateReason.ReplacedByOwner;
            plan = BeginDecommission(channel, ChannelState.Replaced, reason);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await FinishDecommissionAsync(channel, plan);

        var account = await messagingReader.ForAccountAsync(channel.BillingAccountId ?? Guid.Empty, ct: ct);
        var companiesMoved = channel.BillingAccountId is { } accountId
            ? await db.Companies.AsNoTracking().CountAsync(c => c.BillingAccountId == accountId, ct)
            : 0;
        return StatusCode(201, new ReplaceChannelResponseDto(newChannel.Id, account?.For(channel.Transport).Payment.PaidUntil, companiesMoved));
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.28.5 — an old route: a number serves every company of the account, nothing is assigned any more.</summary>
    [HttpPost("{id:guid}/companies")]
    public async Task<IActionResult> AssignCompany(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        return StatusCode(StatusCodes.Status410Gone, "Назначать компании больше не нужно: номер работает для всех ваших компаний");
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.28.5 — an old route: data is not changed.</summary>
    [HttpDelete("{id:guid}/companies/{companyId:guid}")]
    public async Task<IActionResult> UnassignCompany(Guid id, Guid companyId)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>What the provider-side half of a decommission still has to do once the database half is committed.</summary>
    private sealed record DecommissionPlan(string? InstanceId, ChannelCredentials? Credentials, ChannelStateReason Reason, string? StrandedOrphanId = null);

    private async Task DecommissionInstanceAsync(NotificationChannel channel, ChannelState targetState, ChannelStateReason reason)
    {
        var plan = BeginDecommission(channel, targetState, reason);
        // I10: cancelling Pending rows is part of the DB-first step — one transaction, not two.
        var pending = await db.OutboundNotifications
            .Where(n => n.ChannelId == channel.Id && n.Status == NotificationStatus.Pending)
            .ToListAsync();
        foreach (var row in pending)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }
        await db.SaveChangesAsync();
        await FinishDecommissionAsync(channel, plan);
    }

    /// <summary>The database half of releasing a number's provider instance (ARCHITECTURE_CYCLE4.md §30.4: database first, then provider, retry from the
    /// database): the instance goes to <c>OrphanedInstanceId</c>, the secret is blanked, the state change is written with its journal row. Does NOT save.</summary>
    private DecommissionPlan BeginDecommission(NotificationChannel channel, ChannelState targetState, ChannelStateReason reason)
    {
        var instanceId = channel.ProviderInstanceId;
        var fromState = channel.State;

        // I10: best-effort logout with the CHANNEL's own (still valid at the provider) credentials, captured BEFORE they're blanked below.
        ChannelCredentials? credentials = null;
        if (instanceId is not null && channel.ProviderSecretCiphertext is not null)
            credentials = TryDecryptCredentials(channel);

        // An earlier orphan still waiting for deletion must not be overwritten (it would leak a billed instance): it is deleted right
        // away, best effort, by the provider half of the decommission.
        var strandedOrphan = channel.OrphanedInstanceId is { } earlier && instanceId is not null && earlier != instanceId ? earlier : null;

        channel.OrphanedInstanceId = instanceId ?? channel.OrphanedInstanceId;
        channel.ProviderInstanceId = null;
        channel.ProviderSecretCiphertext = null;
        channel.ProviderSecretKeyId = null;
        channel.State = targetState;
        channel.LastStateReason = reason;
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, FromState = fromState, ToState = targetState, Reason = reason,
        });
        return new DecommissionPlan(instanceId, credentials, reason, strandedOrphan);
    }

    private async Task FinishDecommissionAsync(NotificationChannel channel, DecommissionPlan plan)
    {
        if (plan.StrandedOrphanId is not null)
            await TryDeleteInstanceAsync(channel.Transport, plan.StrandedOrphanId);

        if (plan.Credentials is not null)
        {
            try
            {
                await provisioningRegistry.For(channel.Transport).LogoutAsync(plan.Credentials, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Best-effort logout before instance deletion failed, continuing: channelId={ChannelId}", channel.Id);
            }
        }

        if (plan.InstanceId is null) return;

        InstanceDeletion deletion;
        try
        {
            deletion = await provisioningRegistry.For(channel.Transport).DeleteInstanceAsync(plan.InstanceId, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Left as OrphanedInstanceId for ChannelHealthTask to retry (§30.4).
            logger.LogError(ex, "Failed to delete provider instance {InstanceId} for channel {ChannelId}; left orphaned for retry",
                plan.InstanceId, channel.Id);
            return;
        }

        // B6: DeleteInstanceAsync can fail WITHOUT throwing (InstanceDeletion.Success == false).
        if (!deletion.Success)
        {
            logger.LogError("Provider instance deletion did not confirm success for {InstanceId} on channel {ChannelId}; left orphaned for retry",
                plan.InstanceId, channel.Id);
            return;
        }

        if (channel.OrphanedInstanceId == plan.InstanceId) channel.OrphanedInstanceId = null;
        await db.SaveChangesAsync();
        logger.LogWarning("Deleted provider instance {InstanceId} for channel {ChannelId} ({Reason})", plan.InstanceId, channel.Id, plan.Reason);
    }

    /// <summary>Deletes the instance a rebinding left behind, now that the new one exists. A failure leaves <c>OrphanedInstanceId</c> for the
    /// channel-health sweep to retry.</summary>
    private async Task DeleteOldInstanceAsync(NotificationChannel channel, string instanceId, ChannelCredentials? credentials)
    {
        if (credentials is not null)
        {
            try { await provisioningRegistry.For(channel.Transport).LogoutAsync(credentials, CancellationToken.None); }
            catch (Exception ex) { logger.LogDebug(ex, "Best-effort logout of the replaced instance failed: channelId={ChannelId}", channel.Id); }
        }
        if (!await TryDeleteInstanceAsync(channel.Transport, instanceId)) return;
        if (channel.OrphanedInstanceId == instanceId)
        {
            channel.OrphanedInstanceId = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<bool> TryDeleteInstanceAsync(NotificationTransport transport, string instanceId)
    {
        try
        {
            var deletion = await provisioningRegistry.For(transport).DeleteInstanceAsync(instanceId, CancellationToken.None);
            if (!deletion.Success)
                logger.LogError("Provider instance deletion did not confirm success for {InstanceId}; left for retry", instanceId);
            return deletion.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete provider instance {InstanceId}; left for retry", instanceId);
            return false;
        }
    }

    private ChannelCredentials? TryDecryptCredentials(NotificationChannel channel)
    {
        try
        {
            return DecryptCredentials(channel);
        }
        catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
        {
            logger.LogDebug(ex, "Could not decrypt channel secret for a best-effort logout, skipping logout: channelId={ChannelId}", channel.Id);
            return null;
        }
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
    /// неотличим от несуществующего").</summary>
    private async Task<NotificationChannel?> LoadOwnedChannelAsync(Guid id, bool forUpdate = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.NotificationChannels.AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();

        var channel = await query.FirstOrDefaultAsync(c => c.Id == id);
        return channel is not null && channel.OwnerUserId == userId ? channel : null;
    }
}
