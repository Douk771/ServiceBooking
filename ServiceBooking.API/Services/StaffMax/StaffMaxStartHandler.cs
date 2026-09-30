using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498.2–§498.4 — the staff half of the shared MAX webhook: claims a <c>bot_started</c> whose payload starts with "sm1." and
/// binds the chat that sent it to the account that requested the link; on <c>bot_stopped</c> marks every link of that chat as stopped and erases the
/// chat id. Never throws to the webhook: the caller (<c>MaxWebhookHandler</c>) is bound by "always 200".
/// </summary>
public sealed class StaffMaxStartHandler(
    AppDbContext db, StaffMaxAvailability availability, StaffMaxLinkService links, IOptions<PhoneVerificationOptions> phoneOptions,
    IOptions<NotificationOptions> notificationOptions, IMaxBotClient botClient, ILogger<StaffMaxStartHandler> logger) : IMaxBotUpdateHandler
{
    public bool CanHandleStart(string payload) => StaffMaxPayload.IsStaffPayload(payload);

    public async Task HandleStartAsync(MaxUpdateData update, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(update.Payload) || string.IsNullOrEmpty(update.ChatId))
        {
            logger.LogInformation("staff-max webhook: bot_started without a payload or a chat id");
            return;
        }

        var now = DateTime.UtcNow;
        var hash = StaffMaxPayload.Hash(update.Payload);
        var chatKey = StaffMaxChatKey.Compute(phoneOptions.Value.ExternalKeyHmac ?? string.Empty, update.ChatId);
        string reply;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var session = await db.StaffMaxLinkSessions.FirstOrDefaultAsync(s => s.PayloadHash == hash, ct);
            if (session is null || session.ExpiresAtUtc <= now)
                reply = StaffMaxTexts.LinkExpired;
            else if (session.CompletedAtUtc is not null)
            {
                // A redelivered update (or a second press of "Начать") of an already used link.
                var existing = await db.StaffMaxLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == session.UserId, ct);
                reply = existing is { Status: StaffMaxLinkStatus.Active } && existing.ChatKey == chatKey
                    ? StaffMaxTexts.AlreadyLinked
                    : StaffMaxTexts.LinkExpired;
            }
            else if (!availability.Enabled)
                reply = StaffMaxTexts.Disabled;
            else
            {
                var shops = await links.ShopsOfAsync(session.UserId, ct);
                if (shops.Count == 0)
                    reply = StaffMaxTexts.NoShops;
                else
                {
                    var link = await db.StaffMaxLinks.FirstOrDefaultAsync(l => l.UserId == session.UserId, ct);
                    if (link is null)
                    {
                        link = new StaffMaxLink { Id = Guid.NewGuid(), UserId = session.UserId };
                        db.StaffMaxLinks.Add(link);
                    }

                    var key = notificationOptions.Value.EncryptionKey ?? string.Empty;
                    link.ChatKey = chatKey;
                    link.ChatIdCiphertext = SecretProtector.Encrypt(update.ChatId, key, $"staff-max-link:{link.Id}");
                    link.KeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(key));
                    link.Status = StaffMaxLinkStatus.Active;
                    link.LinkedAtUtc = now;
                    link.StoppedAtUtc = null;
                    link.ConsecutiveFailures = 0;
                    session.CompletedAtUtc = now;
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    reply = StaffMaxTexts.Linked(shops.Select(s => s.Name).ToList());
                }
            }
        }

        await ReplyAsync(update.ChatId, reply, ct);
    }

    public async Task HandleStoppedAsync(MaxUpdateData update, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(update.ChatId))
        {
            logger.LogInformation("staff-max webhook: bot_stopped without a chat id");
            return;
        }

        var chatKey = StaffMaxChatKey.Compute(phoneOptions.Value.ExternalKeyHmac ?? string.Empty, update.ChatId);
        await MarkChatStoppedAsync(db, chatKey, DateTime.UtcNow, ct);
    }

    /// <summary>§498.4: every link of the chat becomes <c>StoppedInMax</c> and forgets the chat id. Shared with the dispatcher (403/404 on send).</summary>
    public static Task<int> MarkChatStoppedAsync(AppDbContext db, string chatKey, DateTime nowUtc, CancellationToken ct) =>
        db.StaffMaxLinks.Where(l => l.ChatKey == chatKey && l.Status == StaffMaxLinkStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, StaffMaxLinkStatus.StoppedInMax)
                .SetProperty(l => l.ChatIdCiphertext, (string?)null)
                .SetProperty(l => l.StoppedAtUtc, nowUtc), ct);

    private async Task ReplyAsync(string chatId, string text, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, phoneOptions.Value.Max.TimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await botClient.SendMessageAsync(chatId, text, requestContact: false, linkedCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "staff-max webhook: failed to send a reply to the bot chat");
        }
    }
}
