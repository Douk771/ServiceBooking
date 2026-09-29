namespace ServiceBooking.API.DTOs.Billing;

public record TransferSideDto(Guid BillingAccountId, string? AccountName, string? PlanName);

public record TransferNewOwnerDto(string UserId, string Name, bool IsValid, string Relation, bool WillOccupySeat, string Notice);

// ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.1 (US-20-07, LG6) — OwnerChangeRequired
// appended at the end: true only when no new owner was requested AND the company's current owner has
// no link to the receiving account (CanTransfer is then false too); false in every other case.
public record CompanyTransferPreviewDto(
    Guid CompanyId, string CompanyName, int EmployeeCount, TransferSideDto Source, TransferSideDto Target,
    int TargetCompaniesUsed, int? TargetCompaniesLimit, int TargetSeatsUsed, int? TargetSeatsLimit,
    bool CanTransfer, string? BlockReason, bool SeatOverflow, string? SeatOverflowText,
    bool WillDetachFromChannel, int WillCancelPendingNotifications,
    TransferNewOwnerDto? NewOwner, string? OwnerUnchangedNotice, bool OwnerChangeRequired = false);

// ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.2 (Т20-09 п. 1) — ConfirmRightsTransfer
// appended at the end, defaulting to false so an old admin-frontend build that never sends it is
// rejected (400) rather than silently transferring rights nobody confirmed.
public record CompanyTransferInput(
    Guid TargetBillingAccountId, string? NewOwnerUserId, bool ConfirmSeatOverflow = false, string? Comment = null,
    bool ConfirmRightsTransfer = false);

public record CompanyOwnerChangeDto(
    Guid Id, DateTime ChangedAt, string ChangedByName, string? OldOwnerName, string NewOwnerName, bool WithTransfer, string? Comment);
