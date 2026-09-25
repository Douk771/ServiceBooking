namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §342). Typed binding of the top-level <c>Trial</c> configuration
/// section — its own section, its own key, deliberately never <c>PhoneVerification:ExternalKeyHmac</c>
/// or <c>Notifications:EncryptionKey</c> (К2). <see cref="PhoneKeyHmac"/> never lives in git; it is set
/// only via <c>Trial__PhoneKeyHmac</c> on the deployment host — see DEPLOY.md.
/// </summary>
public sealed class TrialOptions
{
    public const string SectionName = "Trial";

    public UniquenessCheckOptions UniquenessCheck { get; set; } = new();

    /// <summary>Base64-encoded key, at least 32 bytes. Empty in every committed config — Д6 fail-closed
    /// behavior at <see cref="TrialPhoneKey.IsKeyUsable"/> is what a missing/short value triggers.</summary>
    public string? PhoneKeyHmac { get; set; }

    /// <summary>К3 — identifies which key a <c>TrialPhoneRegistration</c> row was computed with. Key
    /// rotation = a new value here; old rows carrying the previous id are destroyed by the retention
    /// rule, never re-compared against anything.</summary>
    public string? PhoneKeyId { get; set; }

    public sealed class UniquenessCheckOptions
    {
        /// <summary>Д17: named "UniquenessCheck", never "Antifraud". <c>false</c> is only legitimate in
        /// tests — <c>DeploymentSafetyChecks</c> refuses to start a Production instance with this off.</summary>
        public bool Enabled { get; set; } = true;
    }
}
