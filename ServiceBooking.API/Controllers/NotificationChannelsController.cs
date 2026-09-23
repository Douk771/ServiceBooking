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
    SubscriptionResolver subscriptionResolver,
    BillingAccountProvisioner billingAccountProvisioner,
    PlatformSettings platformSettings,
    LegalDocumentProvider legalProvider,
    ConsentLedger ledger,
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
        var funding = await LoadFundingAsync(channels);
        return Ok(new ChannelListDto(channels.Select(c => MapToDto(c, idleDays, funding)).ToList()));
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
            PricePerMonth: price, AllowedByPlan: plan.AllowNotificationChannel,
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
        var plan = accountId.HasValue
            ? await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId.Value)
            : EffectivePlan.Free;
        if (!plan.AllowNotificationChannel)
            return StatusCode(402, "Подключение канала недоступно на вашем тарифе");

        // N21, §59/§47.3: `notifications.channel.price-per-month` no longer controls anything — the
        // channel is priced through the `notifications.whatsapp` subscription option now, not this
        // platform setting (which is being removed from the admin screen for the same reason). Gating
        // the request on it being non-null blocked every request the moment nobody had bothered to keep
        // a now-decorative setting non-null, with no way for an owner to tell why.

        // accountId is guaranteed here — GetOffer/AllowNotificationChannel above already required a
        // usable plan, and a usable plan requires an AccountSubscription, which requires an account
        // (BillingAccountProvisioner.EnsureAccountAsync is idempotent if one already exists).
        var ownerAccountId = accountId ?? await billingAccountProvisioner.EnsureAccountAsync(userId);
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            BillingAccountId = ownerAccountId,
            // ARCHITECTURE_CYCLE9.md §104.2/§114.2 (US-119): absent → WhatsApp, so a pre-cycle-9 caller
            // that never sends this field keeps requesting exactly what it always requested. An
            // unparseable string in the JSON body never reaches here at all — [ApiController]'s own model
            // binding already 400s a value that doesn't match NotificationTransport before this action runs.
            Transport = dto.Transport ?? NotificationTransport.WhatsApp,
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
        var funding = await LoadFundingAsync([channel]);
        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, MapToDto(channel, idleDays, funding));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelDto>> GetById(Guid id)
    {
        var channel = await LoadOwnedChannelAsync(id);
        if (channel is null) return NotFound();

        var idleDays = await PlatformIdleDaysAsync();
        var funding = await LoadFundingAsync([channel]);
        return Ok(MapToDto(channel, idleDays, funding));
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
        // §47.3: Connect can only bind a FUNDED number — ChannelFunding.Rank over the account's own
        // live channels, not the channel's own (historical) PaidUntilUtc.
        var funded = await IsChannelFundedAsync(channel, plan);
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
    [RequiresOwnerTerms]
    public async Task<ActionResult<ChannelDto>> AssignCompany(Guid id, [FromBody] AssignCompanyDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var channel = await LoadOwnedChannelAsync(id, forUpdate: true);
        if (channel is null) return NotFound();

        var company = await db.Companies.FindAsync(dto.CompanyId);
        if (company is null || company.OwnerUserId != userId) return Forbid();

        // ARCHITECTURE_CYCLE7.md §43.6/§56 last bullet — "a number doesn't serve a company from a
        // different account". Checked here for a clean 403 with a message; the composite FK on
        // ChannelCompanyAssignment (stage 6) also makes this impossible at the database level, so this
        // check and that constraint can never disagree. OwnerUserId matching above is a rights check,
        // not a money check — this is the money check.
        if (channel.BillingAccountId.HasValue && company.BillingAccountId.HasValue &&
            channel.BillingAccountId != company.BillingAccountId)
            return Forbid();
        if (channel.BillingAccountId is null || company.BillingAccountId is null)
            return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"channel-assignment:{id}");

        // ARCHITECTURE_CYCLE9.md §104.3/§114.3 (US-119): the invariant widened from "one channel ever" to
        // "one channel PER TRANSPORT" — the uniqueness this lookup guards is now (CompanyId, Transport),
        // matching the database's own unique index exactly. A company already on a WhatsApp channel is
        // still a perfectly valid candidate for a MAX channel (a DIFFERENT transport's existing
        // assignment simply doesn't match this WHERE and falls through to a normal new assignment below).
        var existingAssignment = await db.ChannelCompanyAssignments
            .FirstOrDefaultAsync(a => a.CompanyId == dto.CompanyId && a.Transport == channel.Transport);

        if (existingAssignment is not null)
        {
            if (existingAssignment.ChannelId == id)
            {
                // Idempotent re-assignment of the same company to the same channel — no-op 201.
                var idleDaysSame = await PlatformIdleDaysAsync();
                await transaction.CommitAsync();
                return await BuildAssignedResponseAsync(channel, idleDaysSame);
            }
            return Conflict("Салон уже привязан к другому номеру этого мессенджера");
        }

        var otherCompanyCount = await db.ChannelCompanyAssignments.CountAsync(a => a.ChannelId == id);
        if (otherCompanyCount > 0 && !dto.WarningAcknowledged)
            return Conflict("Требуется подтверждение: несколько салонов на одном номере");

        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(),
            ChannelId = id,
            CompanyId = dto.CompanyId,
            BillingAccountId = channel.BillingAccountId!.Value,
            // ARCHITECTURE_CYCLE9.md §104.3: the denormalized copy the composite FK pins to — MUST match
            // channel.Transport exactly, or the FK on (ChannelId, BillingAccountId, Transport) rejects
            // the insert outright.
            Transport = channel.Transport,
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
        var funding = await LoadFundingAsync([channel]);
        return StatusCode(201, MapToDto(channel, idleDays, funding));
    }

    // §47.1/§47.2: single-channel convenience over LoadFundingAsync.
    private async Task<bool> IsChannelFundedAsync(NotificationChannel channel, EffectivePlan plan)
    {
        if (channel.BillingAccountId is not { } accountId) return false;
        var siblings = await db.NotificationChannels.AsNoTracking().Where(c => c.BillingAccountId == accountId).ToListAsync();
        var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
        return ranking.TryGetValue(channel.Id, out var state) && state == ChannelFundingState.Funded;
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE7.md §47.1 — funding state + user-facing text for every channel in
    /// <paramref name="channels"/>, grouped by billing account (normally one account per request here:
    /// this is an owner's own channel list, not an admin-wide scan, so a query per distinct account is
    /// acceptable — unlike CompaniesController's list endpoints, this is not the R11 hot path).
    /// </summary>
    private async Task<Dictionary<Guid, (ChannelFundingState State, string Text, DateTime? PaidUntil)>> LoadFundingAsync(IReadOnlyList<NotificationChannel> channels)
    {
        var result = new Dictionary<Guid, (ChannelFundingState, string, DateTime?)>();
        var accountIds = channels.Where(c => c.BillingAccountId.HasValue).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        if (accountIds.Count == 0) return result;

        var plans = await subscriptionResolver.GetEffectivePlansForAccountsAsync(accountIds);
        foreach (var accountId in accountIds)
        {
            var plan = plans.TryGetValue(accountId, out var p) ? p : EffectivePlan.Free;
            var siblings = await db.NotificationChannels.AsNoTracking().Where(c => c.BillingAccountId == accountId).ToListAsync();
            var live = siblings.Where(c => c.State != ChannelState.Replaced).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
            var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
            var workingChannel = live.FirstOrDefault(c => ranking.TryGetValue(c.Id, out var s) && s == ChannelFundingState.Funded);
            var workingMasked = workingChannel?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(workingChannel.PhoneNumber);

            // N6, §47.3: ChannelDto.paidUntil's SOURCE is the account's notifications.whatsapp option,
            // not the channel's own (no-longer-written) PaidUntilUtc column. A row with no own
            // PaidUntilUtc rides the subscription's own paid period instead (same convention
            // SubscriptionResolver uses), so falls back to the subscription's PaidUntil.
            var whatsappOption = await db.AccountSubscriptionOptions
                .Include(o => o.Option)
                .Where(o => o.BillingAccountId == accountId && o.Option.Code == SubscriptionResolver.WhatsAppOptionCode)
                .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > DateTime.UtcNow)
                .FirstOrDefaultAsync();
            var subPaidUntil = whatsappOption?.PaidUntilUtc is null
                ? await db.AccountSubscriptions.Where(s => s.BillingAccountId == accountId).Select(s => s.PaidUntil).FirstOrDefaultAsync()
                : null;
            var optionPaidUntil = whatsappOption?.PaidUntilUtc ?? subPaidUntil;

            foreach (var c in siblings)
            {
                var state = ranking.TryGetValue(c.Id, out var s2) ? s2 : ChannelFundingState.NotPaid;
                var text = BillingTexts.FundingText(state, plan.PaidNotificationNumbers, live.Count, workingMasked);
                result[c.Id] = (state, text, optionPaidUntil);
            }
        }
        return result;
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
        var query = db.NotificationChannels.Include(c => c.Assignments).ThenInclude(a => a.Company).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();

        var channel = await query.FirstOrDefaultAsync(c => c.Id == id);
        return channel is not null && channel.OwnerUserId == userId ? channel : null;
    }

    private Task<int> PlatformIdleDaysAsync() => platformSettings.GetChannelIdleDaysAsync();

    // §47.3: PaymentState/PaidFrom/PaidUntil keep their FORM but their SOURCE is now `funding` (the
    // account's subscription-driven ranking), not the channel's own historical PaidFromUtc/PaidUntilUtc
    // columns — those are read here ONLY as the (deprecated, always-null-for-PaidFrom) legacy fields the
    // contract still exposes, never to decide payment state.
    private static ChannelDto MapToDto(
        NotificationChannel channel, int idleDays, IReadOnlyDictionary<Guid, (ChannelFundingState State, string Text, DateTime? PaidUntil)> funding)
    {
        var (fundingState, fundingText, subscriptionPaidUntil) = funding.TryGetValue(channel.Id, out var f)
            ? f
            : (ChannelFundingState.NotPaid, BillingTexts.FundingText(ChannelFundingState.NotPaid, 0, 1, null), (DateTime?)null);
        var paymentState = fundingState switch
        {
            ChannelFundingState.Funded => ChannelPaymentStatus.Paid,
            _ when channel.IsSuspendedByAdmin => ChannelPaymentStatus.Suspended,
            _ => ChannelPaymentStatus.NotPaid,
        };
        var phoneMasked = channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber);
        // N6, §47.3: StateText/ChannelDto.paidUntil both read the SUBSCRIPTION's paid-until now, not
        // channel.PaidUntilUtc (that column is no longer written by anything — see the field's own
        // remarks below).
        var stateText = ChannelPresentation.StateText(channel.State, phoneMasked, idleDays, subscriptionPaidUntil, channel.LastStateReason);
        var riskAccepted = channel.RiskAcceptedAtUtc is not null;

        return new ChannelDto(
            channel.Id, channel.Transport, channel.State, stateText, phoneMasked, paymentState,
            PaidFrom: null, // §47.3: deprecated, always null — PaidFromUtc is a historical column, not read.
            subscriptionPaidUntil, channel.RequestedAtUtc, channel.ConnectedAtUtc,
            channel.RiskAcceptedAtUtc, channel.IdleSinceUtc,
            channel.IdleSinceUtc is not null ? channel.IdleSinceUtc.Value.AddDays(idleDays) : null,
            channel.ReplacedByChannelId,
            channel.Assignments.Select(a => new ChannelCompanyDto(a.CompanyId, a.Company.Name, a.Company.IsActive)).ToList(),
            CanConnect: ChannelPresentation.CanConnect(channel.State, paymentState, riskAccepted),
            CanReplace: ChannelPresentation.CanReplace(channel.State),
            FundingState: fundingState,
            FundingText: fundingText,
            Inn: channel.Inn, LegalEntityForm: channel.LegalEntityForm);
    }
}
