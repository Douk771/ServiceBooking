namespace ServiceBooking.Core.Entities;

public class WeeklyScheduleTemplate
{
    public Guid Id { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public int DayOfWeek { get; set; } // 1=Пн, 2=Вт, ..., 7=Вс (ISO)
    public bool IsWorking { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public AppUser Master { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
