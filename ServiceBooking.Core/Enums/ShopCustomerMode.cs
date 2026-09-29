namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — who may place an order in a shop. Persisted as a number: append-only.</summary>
public enum ShopCustomerMode
{
    /// <summary>Anyone, by name and phone (a guest additionally passes the captcha).</summary>
    Anyone = 0,

    /// <summary>Only a signed-in customer whose phone is verified through MAX (VerifiedPhones row).</summary>
    VerifiedPhoneOnly = 1
}
