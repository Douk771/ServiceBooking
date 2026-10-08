using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12.1 — the single point deciding who is told what. The table is the pure <see cref="StayNotificationPlan"/>; queueing of the
/// staff push / MAX and the guest web-push / messenger rows is filled in by the notifications task (BE-37-6). Never calls SaveChanges.
/// </summary>
public partial class StayNotificationPlanner
{
    public virtual Task OnEventAsync(StayBooking booking, StayBookingEvent ev) => Task.CompletedTask;
}
