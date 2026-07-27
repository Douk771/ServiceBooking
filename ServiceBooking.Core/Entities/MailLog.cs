namespace ServiceBooking.Core.Entities;
public class MailLog
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SentById { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
    public AppUser SentBy { get; set; } = null!;
}
