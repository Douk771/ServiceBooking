namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.1 — product type of a company. Persisted as a number: append-only, never
/// reorder or renumber (Services = 0 is the column default every existing row carries).
/// </summary>
public enum CompanyKind
{
    /// <summary>A salon / service business — online booking on ezbook.ru.</summary>
    Services = 0,

    /// <summary>A shop or canteen taking pickup orders on goods.ezbook.ru.</summary>
    Orders = 1,

    /// <summary>ARCHITECTURE_CYCLE37.md §37.3 — a company renting houses by the night on dom.ezbook.ru.</summary>
    Stays = 2,

    /// <summary>ARCHITECTURE_CYCLE42.md §42.2.1 — a company renting baths by the hour on bani.ezbook.ru.</summary>
    Baths = 3
}
