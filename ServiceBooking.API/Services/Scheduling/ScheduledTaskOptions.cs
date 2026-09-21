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
        // Cycle 4, §27.2: PeriodSeconds, when present, wins over PeriodMinutes — the smallest period
        // PeriodMinutes can express is a full minute, but NotificationDispatchTestFactory's tests need a
        // second dispatch pass within a few real seconds. Only the dedicated dispatch-test host ever sets
        // this; every existing task and every other environment keeps using PeriodMinutes untouched.
        var periodSeconds = section.GetValue<int?>("PeriodSeconds");
        var maxRunMinutes = section.GetValue("MaxRunMinutes", 10);

        var period = periodSeconds.HasValue ? TimeSpan.FromSeconds(periodSeconds.Value)
            : periodMinutes.HasValue ? TimeSpan.FromMinutes(periodMinutes.Value)
            : task.DefaultPeriod;

        return new ScheduledTaskOptions(enabled, period, TimeSpan.FromMinutes(maxRunMinutes));
    }
}
