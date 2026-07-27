namespace ServiceBooking.Core.Entities;

public class ClientNote
{
    public Guid Id { get; set; }
    // The company the note was written in. Notes are shared across all masters of this company for the
    // same client (so a master seeing a new booking sees prior notes from colleagues), but NOT across
    // different companies. MasterId still records the author, who alone may delete it.
    public Guid CompanyId { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? GuestPhone { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
    public AppUser Master { get; set; } = null!;
    public AppUser? Client { get; set; }
}
