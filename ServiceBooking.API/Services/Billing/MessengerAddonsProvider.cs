using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.14 (Т40-L-11) — the price lines under the tariff cards, from the option catalog, the platform's availability switches and the published
/// offer: one line per SELLABLE transport (a closed WhatsApp gives none), the note (conditions link, tax note) only when there is at least one line.
/// </summary>
public sealed class MessengerAddonsProvider(AppDbContext db, PlatformSettings platformSettings, LegalDocumentProvider legalDocuments)
{
    public const string ConditionsUrl = "/offer-channel";
    public const string ConditionsLabel = "Условия";

    public async Task<(IReadOnlyList<MessengerAddonDto> Addons, MessengerAddonsNoteDto? Note)> BuildAsync(string taxNote, CancellationToken ct = default)
    {
        var rows = await db.SubscriptionOptions.AsNoTracking()
            .Where(o => o.Code == ChannelOptionCodes.WhatsApp || o.Code == ChannelOptionCodes.Max).ToListAsync(ct);
        var snapshot = legalDocuments.Current;
        var facts = new List<MessengerAddonOptionFacts>();
        foreach (var (code, transport) in new[] { (ChannelOptionCodes.WhatsApp, NotificationTransport.WhatsApp), (ChannelOptionCodes.Max, NotificationTransport.Max) })
        {
            var row = rows.FirstOrDefault(r => r.Code == code);
            facts.Add(new MessengerAddonOptionFacts(
                code, await platformSettings.IsOptionOpenAsync(transport, ct), row?.IsActive ?? false, row?.PricePerMonth, LegalOptionGuards.IsPubliclySellable(code, snapshot)));
        }

        var result = MessengerAddonsBuilder.Build(facts);
        var addons = result.Addons.Select(a => new MessengerAddonDto(a.Transport.ToString(), a.Label, a.PricePerMonth, a.Text, a.Footnote)).ToList();
        return (addons, result.NoteShown ? new MessengerAddonsNoteDto(ConditionsUrl, ConditionsLabel, taxNote) : null);
    }
}
