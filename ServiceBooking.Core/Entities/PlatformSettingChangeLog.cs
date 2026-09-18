namespace ServiceBooking.Core.Entities;

/// <summary>Journal of <see cref="PlatformSetting"/> changes (US-57 pp. 5–6).</summary>
public class PlatformSettingChangeLog
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Comment { get; set; }
}
