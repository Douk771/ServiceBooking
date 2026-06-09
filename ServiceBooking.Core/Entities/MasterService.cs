namespace ServiceBooking.Core.Entities;

public class MasterService
{
    public Guid Id { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public Guid ServiceId { get; set; }

    public AppUser Master { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
