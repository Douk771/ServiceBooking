namespace ServiceBooking.Services;

public class TimeSlot
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public bool IsAvailable { get; set; } = true;
}