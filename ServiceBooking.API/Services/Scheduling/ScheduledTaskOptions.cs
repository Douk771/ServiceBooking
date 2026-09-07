namespace ServiceBooking.API.Services.Scheduling;

/// <summary>Resolved per-task configuration (US-21 p.2): whether it's enabled, how often it runs, and
/// its time budget. Reads <c>ScheduledTasks:{Name}:*</c>, falling back to the task's own
/// <see cref="IScheduledTask.DefaultPeriod"/> when no period is configured.</summary>
public sealed record ScheduledTaskOptions(bool Enabled, TimeSpan Period, TimeSpan MaxRunTime)
{
    public static ScheduledTaskOptions For(IConfiguration config, IScheduledTask task)
    {
        var section = config.GetSection($"ScheduledTasks:{task.Name}");
        var enabled = section.GetValue("Enabled", true);
        var periodMinutes = section.GetValue<int?>("PeriodMinutes");
        var maxRunMinutes = section.GetValue("MaxRunMinutes", 10);

        return new ScheduledTaskOptions(
            enabled,
            periodMinutes.HasValue ? TimeSpan.FromMinutes(periodMinutes.Value) : task.DefaultPeriod,
            TimeSpan.FromMinutes(maxRunMinutes));
    }
}
