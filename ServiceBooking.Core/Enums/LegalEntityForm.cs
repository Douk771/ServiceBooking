namespace ServiceBooking.Core.Enums;

/// <summary>The applicant's legal form for a paid notification channel — API_CONTRACT_CYCLE5.md §50.1
/// (US-82). Purely descriptive (which ИНН length to expect, 10 for Company, 12 for Ip/SelfEmployed) —
/// does not gate anything on its own.</summary>
public enum LegalEntityForm
{
    Ip,
    Company,
    SelfEmployed
}
