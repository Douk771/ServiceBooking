using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Companies;

/// <summary>API_CONTRACT_CYCLE42.md — the 400/409 texts of the staff position rules by company kind («Дома»: управляющий/горничная, «Бани»: администратор/банщик).</summary>
public static class StaffPositionTexts
{
    /// <summary>Unchanged cycle-37 text for a kind without positions (salon, shop); byte-for-byte, existing tests assert it.</summary>
    public const string PositionOnlyForText = "Должность задаётся только сотрудникам компании «Дома».";

    public static (string OnlyStaff, string Position) For(CompanyKind kind) => kind switch
    {
        CompanyKind.Stays => ("В компанию «Дома» можно добавить только сотрудника.", "Укажите должность: управляющий или горничная."),
        CompanyKind.Baths => ("В компанию «Бани» можно добавить только сотрудника.", "Укажите должность: администратор или банщик."),
        CompanyKind.Services or CompanyKind.Orders => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind has no staff positions."),
        _ => throw new System.Diagnostics.UnreachableException()
    };
}
