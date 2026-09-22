namespace ServiceBooking.Core.Enums;

/// <summary>US-74 (ARCHITECTURE_CYCLE5.md §44.4) — the kind of request a data subject is making.</summary>
public enum SubjectRequestKind
{
    Access,
    Rectification,
    Erasure,
    ConsentWithdrawal,
    Complaint
}

/// <summary>Lifecycle of one <see cref="Entities.SubjectRequest"/> — a human answers it, nothing here is
/// automated (ARCHITECTURE_CYCLE5.md §50.1, ПЛ4: "данные не выдаются автоматически").</summary>
public enum SubjectRequestStatus
{
    Received,
    InProgress,
    Answered,
    Rejected
}
