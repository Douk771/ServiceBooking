namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE24.md §448.2, §452.3 — the menu of one shop day: when a row exists, ONLY its items are sold that day.</summary>
public class ShopDailyMenu
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }

    public ICollection<ShopDailyMenuItem> Items { get; set; } = [];
}

/// <summary>A product of a daily menu. PK (DailyMenuId, ProductId).</summary>
public class ShopDailyMenuItem
{
    public Guid DailyMenuId { get; set; }
    public ShopDailyMenu DailyMenu { get; set; } = null!;
    public Guid ProductId { get; set; }
}
