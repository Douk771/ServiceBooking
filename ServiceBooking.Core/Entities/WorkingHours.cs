namespace ServiceBooking.Core.Entities;

public class WorkingHours
{
    public Guid Id { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsWorking { get; set; } = true;

    public AppUser Master { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public ICollection<ScheduleBreak> Breaks { get; set; } = [];
}
