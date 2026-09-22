namespace ServiceBooking.API.DTOs.Billing;

public record TransferSideDto(Guid BillingAccountId, string? AccountName, string? PlanName);

public record TransferNewOwnerDto(string UserId, string Name, bool IsValid, string Relation, bool WillOccupySeat, string Notice);

public record CompanyTransferPreviewDto(
    Guid CompanyId, string CompanyName, int EmployeeCount, TransferSideDto Source, TransferSideDto Target,
    int TargetCompaniesUsed, int? TargetCompaniesLimit, int TargetSeatsUsed, int? TargetSeatsLimit,
    bool CanTransfer, string? BlockReason, bool SeatOverflow, string? SeatOverflowText,
    bool WillDetachFromChannel, int WillCancelPendingNotifications,
    TransferNewOwnerDto? NewOwner, string? OwnerUnchangedNotice);

public record CompanyTransferInput(Guid TargetBillingAccountId, string? NewOwnerUserId, bool ConfirmSeatOverflow = false, string? Comment = null);

public record CompanyOwnerChangeDto(
    Guid Id, DateTime ChangedAt, string ChangedByName, string? OldOwnerName, string NewOwnerName, bool WithTransfer, string? Comment);
