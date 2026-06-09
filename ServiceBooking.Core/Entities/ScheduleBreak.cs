namespace ServiceBooking.Core.Entities;

public class ScheduleBreak
{
    public Guid Id { get; set; }
    public Guid WorkingHoursId { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public WorkingHours WorkingHours { get; set; } = null!;
}
