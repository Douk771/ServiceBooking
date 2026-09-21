namespace ServiceBooking.Core.Entities;

/// <summary>
/// Health/contraindication information about one client, for one company — US-77
/// (ARCHITECTURE_CYCLE5.md §44.3). A dedicated table, deliberately NOT a column on <see cref="ClientNote"/>:
/// a client has many ordinary notes but at most one health note, and putting it on ClientNote would mean
/// any future projection of that entity could pick the field up "for free" — the whole point of this
/// table is that leaking it is mechanically impossible (there is no navigation property from ClientNote
/// to this type anywhere in the model).
///
/// Exactly one of <see cref="ClientId"/>/<see cref="GuestPhone"/> is set, mirroring <see cref="ClientNote"/>'s
/// own client-vs-guest split — enforced by the two partial unique indexes in AppDbContext, not by a CHECK
/// constraint (matches this codebase's existing convention for ClientNote/ClientNotePhoto).
/// </summary>
public class ClientHealthNote
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? ClientId { get; set; }
    public string? GuestPhone { get; set; }

    // §48.1: v1.<keyId>.<base64(nonce‖tag‖ct)> — same format SecretProtector already produces for
    // channel secrets, AAD bound to "company + subject" instead of "channel id" (HealthNoteProtector).
    public string Ciphertext { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }

    public Company Company { get; set; } = null!;
    public AppUser? Client { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
