namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// ARCHITECTURE_CYCLE15.md §252.3/§257.4 — the single rule for "how close to a visit a CLIENT (never
/// staff) may still reschedule it", by the same convention as <see cref="ServiceBooking.API.Services.BookingHorizon"/>.
/// Pure, no EF types. Used at exactly two points: CompaniesController.Update (saving the setting) and
/// BookingsController.Reschedule's client-only path (checking BOTH ends of the window, §257.4).
/// ARCHITECTURE_CYCLE17.md §304.1 — and cancel: the same Company.ClientRescheduleMinHours also gates
/// the client-owner's cancel path (BookingsController.Cancel), deliberately without a separate field.
/// </summary>
public static class ClientRescheduleWindow
{
    public const int Default = 2;
    public const int Min = 0;
    public const int Max = 168;

    /// <summary>
    /// False only when a value was actually supplied and falls outside [Min, Max] — the one source of
    /// the 400 in CompaniesController.Update. Null/omitted -> false (caller must not touch the stored
    /// value at all — unlike BookingHorizon, 0 here is a legitimate explicit value ["до самого начала
    /// визита"], not a reset-to-default sentinel, so it does not get special-cased).
    /// </summary>
    public static bool TryNormalize(int? raw, out int hours)
    {
        if (raw is null)
        {
            hours = 0;
            return false;
        }

        if (raw.Value < Min || raw.Value > Max)
        {
            hours = 0;
            return false;
        }

        hours = raw.Value;
        return true;
    }

    /// <summary>Defends against garbage that somehow ended up in the DB (a value outside [Min, Max])
    /// by falling back to Default — mirrors BookingHorizon.Normalize's own defensive read.</summary>
    public static int Normalize(int stored) => stored < Min || stored > Max ? Default : stored;

    /// <summary>
    /// ARCHITECTURE_CYCLE15.md §257.4 — true only when BOTH the current visit's start and the requested
    /// new visit's start are still at least <paramref name="minHours"/> away from `nowUtc`. Checking only
    /// the old end would let a client reschedule a visit three days out to "twenty minutes from now";
    /// checking only the new end would let them move a visit that starts in five minutes.
    /// </summary>
    public static bool IsWithinWindow(DateTime nowUtc, DateTime currentVisitStartUtc, DateTime newVisitStartUtc, int minHours)
    {
        var cutoff = TimeSpan.FromHours(minHours);
        return nowUtc <= currentVisitStartUtc - cutoff && nowUtc <= newVisitStartUtc - cutoff;
    }

    /// <summary>ARCHITECTURE_CYCLE17.md §304.1 — client CANCEL: one end of the window, not two (there
    /// is no "new" visit to also check, unlike reschedule). True = still allowed to cancel.</summary>
    public static bool CanClientCancel(DateTime nowUtc, DateTime visitStartUtc, int minHours)
        => nowUtc <= visitStartUtc - TimeSpan.FromHours(minHours);
}
