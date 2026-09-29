namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — a catalog category of a shop. Deleted for real, only when empty.</summary>
public class ProductCategory
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Order among the shop's categories; compacted by the server (CatalogOrdering).</summary>
    public int Position { get; set; }

    /// <summary>Products of a hidden category are not shown to customers.</summary>
    public bool IsHidden { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
