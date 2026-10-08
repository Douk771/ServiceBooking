using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Cycle 39 appended ManageServices, EditServiceContent, ManageServiceDates (ARCHITECTURE_CYCLE39.md §39.12): append-only, the names travel to the frontend.</summary>
public enum StaysPermission
{
    ManageCompany, ManageHouses, EditHouseContent, ViewBookings, ManageBookings, ManageBlocks, ViewSchedule, ViewCabinet,
    ManageServices, EditServiceContent, ManageServiceDates
}

public enum StaysMyRole { Owner, Manager, Housekeeper, SuperAdmin }

/// <summary>ARCHITECTURE_CYCLE37.md §37.9 — the ONE table of permissions of the "Дома" vertical.</summary>
public static class StaysAccess
{
    private static readonly StaysPermission[] All = Enum.GetValues<StaysPermission>();

    private static readonly StaysPermission[] ManagerSet =
    [
        StaysPermission.EditHouseContent, StaysPermission.ViewBookings, StaysPermission.ManageBookings,
        StaysPermission.ManageBlocks, StaysPermission.ViewSchedule, StaysPermission.ViewCabinet,
        StaysPermission.EditServiceContent, StaysPermission.ManageServiceDates
    ];

    public static IReadOnlyList<StaysPermission> For(StaysMyRole role) => role switch
    {
        StaysMyRole.Owner or StaysMyRole.SuperAdmin => All,
        StaysMyRole.Manager => ManagerSet,
        _ => [StaysPermission.ViewSchedule]
    };

    public static bool Has(StaysMyRole role, StaysPermission permission) => For(role).Contains(permission);

    public static StaysMyRole RoleOfMember(bool isOwner, StaffPosition? position) =>
        isOwner ? StaysMyRole.Owner : position == StaffPosition.Manager ? StaysMyRole.Manager : StaysMyRole.Housekeeper;
}
