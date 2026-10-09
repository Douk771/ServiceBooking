namespace ServiceBooking.API.Services.Notifications.Funding;

/// <summary>ARCHITECTURE_CYCLE40.md §40.2.1, §40.3 — codes of the two channel options and the fixed id of the MAX option seed row.</summary>
public static class ChannelOptionCodes
{
    public const string WhatsApp = "notifications.whatsapp";
    public const string Max = "notifications.max";

    /// <summary>Id of the <c>notifications.max</c> row inserted by the Cycle40ChannelOptions migration.</summary>
    public static readonly Guid MaxOptionSeedId = new("c4000000-0000-4000-8000-000000000040");

    public static readonly IReadOnlyList<string> All = [WhatsApp, Max];
}
