using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

public sealed record MessengerAddonOptionFacts(string Code, bool Open, bool IsActive, decimal? PricePerMonth, bool LegallySellable);

public sealed record MessengerAddon(NotificationTransport Transport, string Label, decimal PricePerMonth, string Text, string? Footnote);

public sealed record MessengerAddonsResult(IReadOnlyList<MessengerAddon> Addons, bool NoteShown);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.14 (Т40-L-11) — the price lines under tariff cards: only SELLABLE transports (open, active,
/// with a price, offer published), order WhatsApp then MAX, "+ MAX 490 ₽/мес", a footnote for WhatsApp only. The note
/// block (conditions link, tax note) is shown iff at least one line exists. Pure. Vectors: <c>addons</c>.
/// </summary>
public static class MessengerAddonsBuilder
{
    // Same literals as the catalog seed (BE-40-M's ChannelOptionCodes carries them as constants).
    private const string WhatsAppCode = "notifications.whatsapp";
    private const string MaxCode = "notifications.max";

    public static MessengerAddonsResult Build(IEnumerable<MessengerAddonOptionFacts> options)
    {
        var addons = new List<MessengerAddon>();
        foreach (var (code, transport) in new[] { (WhatsAppCode, NotificationTransport.WhatsApp), (MaxCode, NotificationTransport.Max) })
        {
            var option = options.FirstOrDefault(o => o.Code == code);
            if (option is null || option.PricePerMonth is not { } price) continue;
            if (!option.Open || !option.IsActive || !option.LegallySellable) continue;

            var label = MessengerTexts.DisplayName(transport);
            var priceText = MessengerTexts.PriceText(price).Replace(" ₽/мес", string.Empty);
            addons.Add(new MessengerAddon(transport, label, price, $"+ {label} {priceText} ₽/мес",
                transport == NotificationTransport.WhatsApp ? MessengerTexts.WhatsAppFootnote : null));
        }
        return new MessengerAddonsResult(addons, addons.Count > 0);
    }
}
