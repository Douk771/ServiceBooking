namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Who a ConsentRecord row is (or is being queried) about (ARCHITECTURE_CYCLE5.md §45.1). Two factories
/// on purpose, mirroring ConsentRecord's two partial indexes exactly — a subject is either an account
/// (ForUser, matches IX_ConsentRecords_CurrentByUser) or a phone number known to one specific company
/// (ForPhoneInCompany, matches IX_ConsentRecords_CurrentBySubject), never constructed with a bare
/// UserId-and-CompanyId or a bare phone-with-no-company: "forgetting about the guest" (§45.1) is made a
/// compile-time-shaped mistake, not a runtime one — there is no third constructor path that would let a
/// caller build a subject that matches neither index.
/// </summary>
public sealed record ConsentSubject
{
    public string? UserId { get; }
    public string? Phone { get; }
    public Guid? CompanyId { get; }

    private ConsentSubject(string? userId, string? phone, Guid? companyId)
    {
        UserId = userId;
        Phone = phone;
        CompanyId = companyId;
    }

    public static ConsentSubject ForUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required.", nameof(userId));
        return new ConsentSubject(userId, null, null);
    }

    public static ConsentSubject ForPhoneInCompany(string phone, Guid companyId)
    {
        if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("phone is required.", nameof(phone));
        if (companyId == Guid.Empty) throw new ArgumentException("companyId is required.", nameof(companyId));
        return new ConsentSubject(null, phone, companyId);
    }

    /// <summary>Stable string for the advisory-lock key (ARCHITECTURE_CYCLE5.md §45.4) — never exposed
    /// to callers outside this namespace, never logged.</summary>
    internal string LockKey => UserId is not null ? $"consent:user:{UserId}" : $"consent:phone:{Phone}:company:{CompanyId}";
}
