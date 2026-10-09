namespace ServiceBooking.API.Services.Billing;

public enum TrialOptionGrantAction
{
    /// <summary>Closed option: no row is created or touched (it is not sold and not granted).</summary>
    SkipClosed,
    /// <summary>No row yet: create the trial row.</summary>
    Create,
    /// <summary>A row the trial itself created earlier: revive it for the new trial.</summary>
    Revive,
    /// <summary>A row that is not the trial's (bought/assigned by an admin): left untouched.</summary>
    LeaveAsIs,
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.5 (О6 with the Р40-Ю1 amendment) — what a trial grant does with ONE channel option: only OPEN options are
/// granted; the tariff's option rule is not read. Opening an option later does not top up a trial already running (the decision is taken
/// at the moment of the grant). Pure.
/// </summary>
public static class TrialOptionGrantRule
{
    public static TrialOptionGrantAction Decide(bool optionOpen, bool rowExists, bool rowGrantedByTrial)
    {
        if (!optionOpen) return TrialOptionGrantAction.SkipClosed;
        if (!rowExists) return TrialOptionGrantAction.Create;
        return rowGrantedByTrial ? TrialOptionGrantAction.Revive : TrialOptionGrantAction.LeaveAsIs;
    }
}
