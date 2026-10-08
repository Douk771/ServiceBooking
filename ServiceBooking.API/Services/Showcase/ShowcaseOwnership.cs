namespace ServiceBooking.API.Services.Showcase;

/// <summary>One deletion (and count) step of the showcase eraser: rows of <see cref="Table"/> matching <see cref="Where"/>.</summary>
public sealed record OwnershipStep(string Report, string Table, string Where)
{
    public string DeleteSql => $"DELETE FROM \"{Table}\" WHERE {Where}";
    public string CountSql => $"SELECT COUNT(*)::int AS \"Value\" FROM \"{Table}\" WHERE {Where}";
}

/// <summary>
/// ARCHITECTURE_CYCLE28.md §572.3 — the single place that says how each table is tied to the showcase mark. Only three roots carry the mark
/// (<c>Companies.IsShowcase</c>, <c>AspNetUsers.IsShowcase</c>, <c>BillingAccounts.IsShowcase</c>); everything else is showcase by the chain of ownership below.
/// The eraser runs <see cref="DeleteSteps"/> in this order (children before parents: physical company deletion is new to the product and several foreign keys
/// are <c>Restrict</c>, so the ORDER is what makes it work — no foreign key behaviour is changed). The planner counts the same steps
/// ("будет удалено N"), and a test walks the EF model to make sure a table added in a future cycle cannot silently escape this list.
/// </summary>
public static class ShowcaseOwnership
{
    private const string Companies = "(SELECT \"Id\" FROM \"Companies\" WHERE \"IsShowcase\")";
    private const string Users = "(SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"IsShowcase\")";
    private const string Accounts = "(SELECT \"Id\" FROM \"BillingAccounts\" WHERE \"IsShowcase\")";
    private const string CompanyBookings = "(SELECT \"Id\" FROM \"Bookings\" WHERE \"CompanyId\" IN " + Companies + ")";
    private const string CompanyServices = "(SELECT \"Id\" FROM \"Services\" WHERE \"CompanyId\" IN " + Companies + ")";
    private const string CompanyOrders = "(SELECT \"Id\" FROM \"Orders\" WHERE \"CompanyId\" IN " + Companies + ")";
    private const string CompanyDailyMenus = "(SELECT \"Id\" FROM \"ShopDailyMenus\" WHERE \"CompanyId\" IN " + Companies + ")";
    private const string CompanyWorkingHours = "(SELECT \"Id\" FROM \"WorkingHours\" WHERE \"CompanyId\" IN " + Companies + ")";

    private static OwnershipStep ByCompany(string report, string table) => new(report, table, $"\"CompanyId\" IN {Companies}");

    private static OwnershipStep ByUser(string table) => new(table, table, $"\"UserId\" IN {Users}");

    /// <summary>Deletion order. Every table that can hold a row belonging to a showcase company, account or user appears here.</summary>
    public static readonly IReadOnlyList<OwnershipStep> DeleteSteps =
    [
        // Everything that hangs off a booking or a company (a visitor of the demo or a staff member may have created some of it).
        ByCompany("bookingEvents", "BookingEvents"),
        new("bookingServices", "BookingServices", $"\"BookingId\" IN {CompanyBookings}"),
        ByCompany("reviews", "Reviews"),
        ByCompany("outboundNotifications", "OutboundNotifications"),
        ByCompany("staffPushNotifications", "StaffPushNotifications"),
        ByCompany("clientNotePhotos", "ClientNotePhotos"),
        ByCompany("clientNotes", "ClientNotes"),
        ByCompany("clientHealthNotes", "ClientHealthNotes"),
        new("consentRecords", "ConsentRecords", $"\"CompanyId\" IN {Companies} OR \"UserId\" IN {Users} OR \"RecordedByUserId\" IN {Users}"),
        ByCompany("bookings", "Bookings"),
        new("masterServices", "MasterServices", $"\"ServiceId\" IN {CompanyServices} OR \"MasterId\" IN {Users}"),
        new("scheduleBreaks", "ScheduleBreaks", $"\"WorkingHoursId\" IN {CompanyWorkingHours}"),
        ByCompany("workingHours", "WorkingHours"),
        ByCompany("scheduleTemplates", "WeeklyScheduleTemplates"),
        ByCompany("photos", "CompanyPhotos"),
        ByCompany("services", "Services"),
        ByCompany("mailLogs", "MailLogs"),
        ByCompany("notificationSettings", "CompanyNotificationSettings"),
        ByCompany("notificationTemplates", "NotificationTemplates"),
        ByCompany("notificationTemplateHistories", "NotificationTemplateHistories"),
        ByCompany("channelAssignments", "ChannelCompanyAssignments"),
        ByCompany("ownerChangeLogs", "CompanyOwnerChangeLogs"),
        new("subscriptionChangeLogs", "SubscriptionChangeLogs", $"\"CompanyId\" IN {Companies} OR \"BillingAccountId\" IN {Accounts}"),
        // ARCHITECTURE_CYCLE35.md §35.9.7 — the shops of «Заказы» (the demo profile, and whatever a visitor of the demo put into a shop of the showcase). Children before
        // parents: OrderItems and ShopDailyMenuItems reference Products with Restrict, Products reference categories, every goods table references Companies with Restrict.
        ByCompany("orderEvents", "OrderEvents"),
        new("orderItems", "OrderItems", $"\"OrderId\" IN {CompanyOrders}"),
        new("orderPushSubscriptions", "OrderPushSubscriptions", $"\"OrderId\" IN {CompanyOrders}"),
        ByCompany("customerOrderPushNotifications", "CustomerOrderPushNotifications"),
        ByCompany("staffMaxMessages", "StaffMaxMessages"),
        ByCompany("orders", "Orders"),
        ByCompany("orderDailyCounters", "OrderDailyCounters"),
        ByCompany("shopCustomerNotes", "ShopCustomerNotes"),
        new("shopDailyMenuItems", "ShopDailyMenuItems", $"\"DailyMenuId\" IN {CompanyDailyMenus}"),
        ByCompany("shopDailyMenus", "ShopDailyMenus"),
        ByCompany("shopSpecialDays", "ShopSpecialDays"),
        ByCompany("products", "Products"),
        ByCompany("productCategories", "ProductCategories"),
        ByCompany("shopSettings", "ShopSettings"),
        new("members", "CompanyMembers", $"\"CompanyId\" IN {Companies} OR \"UserId\" IN {Users}"),
        new("companies", "Companies", "\"IsShowcase\""),

        // Billing accounts of the showcase.
        new("accountSubscriptionOptions", "AccountSubscriptionOptions", $"\"BillingAccountId\" IN {Accounts}"),
        new("accountSubscriptions", "AccountSubscriptions", $"\"BillingAccountId\" IN {Accounts} OR \"OwnerUserId\" IN {Users}"),
        new("ordersSubscriptions", "OrdersSubscriptions", $"\"BillingAccountId\" IN {Accounts}"),
        new("orderMonthlyUsages", "OrderMonthlyUsages", $"\"BillingAccountId\" IN {Accounts}"),
        new("trialGrants", "TrialGrants", $"\"BillingAccountId\" IN {Accounts}"),
        new("billingAccounts", "BillingAccounts", "\"IsShowcase\""),

        // Users of the showcase (owners, masters, clients) and what belongs to them.
        ByUser("PushSubscriptions"),
        ByUser("VerifiedPhones"),
        ByUser("PhoneVerificationSessions"),
        ByUser("StaffMaxLinks"),
        ByUser("StaffMaxLinkSessions"),
        ByUser("PlatformNoticeAcknowledgements"),
        ByUser("AspNetUserRoles"),
        ByUser("AspNetUserClaims"),
        ByUser("AspNetUserLogins"),
        ByUser("AspNetUserTokens"),
        new("users", "AspNetUsers", "\"IsShowcase\""),
    ];

    /// <summary>
    /// Tables that reference a company, a user, a billing account or a booking but are NEVER written for a showcase and are therefore always empty there
    /// (the reason is the value). The coverage test asserts a table is either in <see cref="DeleteSteps"/> or here — a new table must be decided on.
    /// A functional test (QA) checks the emptiness itself.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NeverWritten = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NotificationChannels"] = "showcase accounts own no notification numbers (mixing with real accounts is forbidden, sending is suppressed)",
        ["StaysSettings"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["Houses"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HousePhotos"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HousePricePeriods"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HouseRegistryAttestations"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HouseBlocks"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HouseBlockEvents"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["HouseOccupancies"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayBookings"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayBookingCharges"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayBookingEvents"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayPaymentProofs"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayGuestPushSubscriptions"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayGuestPushNotifications"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StaysSubscriptions"] = "cycle 37: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE37.md §37.3.2)",
        ["StayServices"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServicePhotos"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceWeeklyWindows"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceDateOverrides"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceScheduleEvents"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServicePriceRules"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceItems"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceSessions"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceOrders"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StayServiceOrderEvents"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
        ["StaysReminderTemplateChanges"] = "cycle 39: the \"Дома\" vertical has no showcase (ARCHITECTURE_CYCLE39.md §39.2.6)",
    };

    /// <summary>All tables the eraser touches, for the model coverage test.</summary>
    public static IEnumerable<string> Tables => DeleteSteps.Select(s => s.Table);
}
