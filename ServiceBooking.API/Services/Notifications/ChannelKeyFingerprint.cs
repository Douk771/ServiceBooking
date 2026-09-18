namespace ServiceBooking.API.Services.Notifications;

/// <summary>Outcome of <see cref="ChannelKeyFingerprint.Decide"/> — what the caller must now do.</summary>
public enum ChannelKeyFingerprintOutcome
{
    /// <summary>No fingerprint file existed yet — this is treated as the first deployment. The caller
    /// must write the current fingerprint so the next start has something to compare against.</summary>
    FirstRun,

    /// <summary>Stored fingerprint matches the configured key. Nothing to do.</summary>
    Matched,

    /// <summary>Stored fingerprint matches, but <c>NOTIFICATIONS_KEY_ROTATION_ACK</c> is still set from a
    /// completed rotation — the caller must warn so the operator removes it (§24.5's "leaving it set will
    /// keep warning" rule: a one-shot acknowledgement that is never cleared is not an acknowledgement).</summary>
    MatchedWithLeftoverAck,

    /// <summary>Stored fingerprint does not match, but the rotation was acknowledged by naming the new
    /// key's id — a deliberate, one-shot key rotation (§24.4). The caller must overwrite the fingerprint
    /// file with the new value and warn that the ack should now be removed.</summary>
    RotatedByAck,
}

/// <summary>
/// Detects a silently lost or regenerated <c>NOTIFICATIONS_ENCRYPTION_KEY</c> at startup
/// (ARCHITECTURE_CYCLE4.md §24.5, risk R10). AES-GCM does not fail on an invalid key at process start —
/// it fails one channel at a time, on the first decryption attempt, which reads to an operator as
/// "WhatsApp stopped working for every salon at once" hours after the actual cause (an operator
/// regenerating secrets in bulk after an incident, which the pre-cycle-4 runbook explicitly invited by
/// listing this key alongside ones that genuinely are safe to regenerate).
///
/// Split into a pure decision core (<see cref="Decide"/> — no file IO, no clock, eight cases fully
/// enumerated below and each one a unit test) and a thin IO wrapper (<see cref="ValidateAndPersist"/>)
/// that takes its file-system access as delegates, so the wrapper itself never needs a real host or file
/// system in tests either.
/// </summary>
public static class ChannelKeyFingerprint
{
    /// <summary>SHA-256 hex digest length — the well-formedness check for a stored fingerprint line.</summary>
    public const int FingerprintHexLength = 64;

    /// <summary>Length of the key id embedded in ciphertexts and expected in the rotation ack.</summary>
    public const int KeyIdHexLength = 8;

    /// <summary>
    /// Pure decision core — eight cases, each named so a failing test names the case, not just an
    /// assertion:
    ///
    /// 1. No stored fingerprint (first deployment) → <see cref="ChannelKeyFingerprintOutcome.FirstRun"/>.
    /// 2. Stored fingerprint is not 64 hex characters (file corrupted/tampered/truncated) → throws.
    /// 3. Stored fingerprint matches the current key, no ack set → <see cref="ChannelKeyFingerprintOutcome.Matched"/>.
    /// 4. Stored fingerprint matches, ack still set (leftover from a past rotation) →
    ///    <see cref="ChannelKeyFingerprintOutcome.MatchedWithLeftoverAck"/>.
    /// 5. Mismatch, no ack set (the ordinary "key was lost or regenerated" case) → throws.
    /// 6. Mismatch, ack set but not exactly 8 hex characters (malformed ack) → throws.
    /// 7. Mismatch, ack set but does not name the CURRENT (new) key's id — e.g. it still names the OLD
    ///    key, or is a typo, or is left over from an unrelated earlier rotation attempt → throws.
    /// 8. Mismatch, ack correctly names the current key's id → deliberate rotation →
    ///    <see cref="ChannelKeyFingerprintOutcome.RotatedByAck"/>.
    /// </summary>
    /// <param name="storedFingerprint">The fingerprint line read from the fingerprint file, or
    /// <see langword="null"/>/empty when the file does not exist yet.</param>
    /// <param name="currentFingerprint">64-hex-character SHA-256 of the key this process was started
    /// with (<see cref="SecretProtector.ComputeKeyFingerprint"/>).</param>
    /// <param name="rotationAck"><c>Notifications:KeyRotationAck</c>, or <see langword="null"/>/empty
    /// when not set.</param>
    /// <exception cref="InvalidOperationException">The key that was used to start this process is not
    /// the one the fingerprint file says was used last time, and no valid rotation was acknowledged.</exception>
    public static ChannelKeyFingerprintOutcome Decide(string? storedFingerprint, string currentFingerprint, string? rotationAck)
    {
        if (string.IsNullOrWhiteSpace(storedFingerprint))
            return ChannelKeyFingerprintOutcome.FirstRun;

        var trimmedStored = storedFingerprint.Trim();
        if (!IsWellFormedFingerprint(trimmedStored))
            throw new InvalidOperationException(
                "The channel encryption key fingerprint file is corrupted (expected " +
                $"{FingerprintHexLength} hex characters on its first line). If this is a deliberate key " +
                "rotation, follow the rotation procedure in DEPLOY.md; if not, restore the fingerprint " +
                "file and .env together from the same backup.");

        var trimmedAck = string.IsNullOrWhiteSpace(rotationAck) ? null : rotationAck.Trim();

        if (string.Equals(trimmedStored, currentFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedAck is null
                ? ChannelKeyFingerprintOutcome.Matched
                : ChannelKeyFingerprintOutcome.MatchedWithLeftoverAck;
        }

        // Mismatch: the key this process was started with is not the one recorded last time. This is
        // either a deliberate rotation (ack names the NEW key's id) or a silent loss — refuse to start
        // unless the ack proves the former.
        if (trimmedAck is null)
            throw KeyMismatchException(
                "NOTIFICATIONS_ENCRYPTION_KEY does not match the fingerprint recorded on a previous start, " +
                "and no rotation was acknowledged.");

        if (trimmedAck.Length != KeyIdHexLength || !IsHex(trimmedAck))
            throw KeyMismatchException(
                $"NOTIFICATIONS_KEY_ROTATION_ACK is set but is not a {KeyIdHexLength}-character hex key id.");

        var currentKeyId = currentFingerprint[..KeyIdHexLength];
        if (!string.Equals(trimmedAck, currentKeyId, StringComparison.OrdinalIgnoreCase))
            throw KeyMismatchException(
                $"NOTIFICATIONS_KEY_ROTATION_ACK ('{trimmedAck}') does not match the new key's id " +
                $"('{currentKeyId}') — it names a different key than the one this process was started with.");

        return ChannelKeyFingerprintOutcome.RotatedByAck;
    }

    /// <summary>
    /// IO wrapper around <see cref="Decide"/>: reads the fingerprint file (if any) through the supplied
    /// delegates, decides, and — for <see cref="ChannelKeyFingerprintOutcome.FirstRun"/> and
    /// <see cref="ChannelKeyFingerprintOutcome.RotatedByAck"/> — writes the new fingerprint back, and for
    /// <see cref="ChannelKeyFingerprintOutcome.MatchedWithLeftoverAck"/>/<see cref="ChannelKeyFingerprintOutcome.RotatedByAck"/>
    /// emits a warning. No real file system or host required by the wrapper itself — it only calls what
    /// it is given — which is what lets <c>ChannelKeyFingerprintTests</c> exercise this without a temp
    /// directory.
    /// </summary>
    public static ChannelKeyFingerprintOutcome ValidateAndPersist(
        byte[] keyBytes,
        string? rotationAck,
        string fingerprintPath,
        Func<string, bool> fileExists,
        Func<string, string> readFile,
        Action<string, string> writeFile,
        Action<string> warn)
    {
        var currentFingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);
        var stored = fileExists(fingerprintPath) ? ExtractFingerprintLine(readFile(fingerprintPath)) : null;

        var outcome = Decide(stored, currentFingerprint, rotationAck);

        switch (outcome)
        {
            case ChannelKeyFingerprintOutcome.FirstRun:
                writeFile(fingerprintPath, RenderFileContent(currentFingerprint));
                break;

            case ChannelKeyFingerprintOutcome.RotatedByAck:
                writeFile(fingerprintPath, RenderFileContent(currentFingerprint));
                warn(
                    $"NOTIFICATIONS_ENCRYPTION_KEY was rotated (fingerprint now starts with " +
                    $"{currentFingerprint[..KeyIdHexLength]}). Remove NOTIFICATIONS_KEY_ROTATION_ACK from " +
                    ".env now — every following start will keep warning until you do.");
                break;

            case ChannelKeyFingerprintOutcome.MatchedWithLeftoverAck:
                warn(
                    "NOTIFICATIONS_KEY_ROTATION_ACK is set but the key fingerprint already matches the " +
                    "recorded one — remove it from .env, it is not doing anything and will hide a future " +
                    "genuine mismatch.");
                break;

            case ChannelKeyFingerprintOutcome.Matched:
                break;
        }

        return outcome;
    }

    /// <summary>
    /// First non-blank, non-comment ('#') line of the fingerprint file — the file otherwise carries
    /// human-readable comments explaining what it is (§24.5), which must not be mistaken for the
    /// fingerprint itself.
    /// </summary>
    private static string? ExtractFingerprintLine(string fileContent)
    {
        foreach (var rawLine in fileContent.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            return line;
        }
        return null;
    }

    private static string RenderFileContent(string fingerprint) =>
        "# This file is written and checked by ServiceBooking on every start (ARCHITECTURE_CYCLE4.md " +
        "§24.5).\n" +
        "# It records the SHA-256 fingerprint of NOTIFICATIONS_ENCRYPTION_KEY at the last successful\n" +
        "# start. If the key configured now does not produce this fingerprint, startup refuses to\n" +
        "# continue instead of silently failing to decrypt every WhatsApp channel's token.\n" +
        "# Do not edit this file by hand except as part of the rotation procedure in DEPLOY.md.\n" +
        fingerprint + "\n";

    private static bool IsWellFormedFingerprint(string value) =>
        value.Length == FingerprintHexLength && IsHex(value);

    private static bool IsHex(string value) => value.All(Uri.IsHexDigit);

    private static InvalidOperationException KeyMismatchException(string reason) =>
        new(
            "NOTIFICATIONS_ENCRYPTION_KEY does not match the key this application last started with, and " +
            $"the mismatch was not acknowledged as a deliberate rotation. Reason: {reason} Losing this key " +
            "strands every salon's WhatsApp token — decryption fails one channel at a time, not at " +
            "startup, and looks like 'WhatsApp stopped working for everyone' hours after the actual cause. " +
            "If this key was regenerated by accident (e.g. while rotating other secrets), restore " +
            "NOTIFICATIONS_ENCRYPTION_KEY from the .env backup. If this is a deliberate rotation, follow " +
            "the procedure in DEPLOY.md (set NOTIFICATIONS_KEY_ROTATION_ACK to the new key's id).");
}
