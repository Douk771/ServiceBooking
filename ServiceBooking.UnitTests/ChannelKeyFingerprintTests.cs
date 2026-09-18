using System.Security.Cryptography;
using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE4.md §24.5: the eight decision cases named in <see cref="ChannelKeyFingerprint"/>'s
/// doc comment, each as its own test, plus the IO wrapper exercised with fake in-memory delegates — no
/// real file system, no host.
/// </summary>
public class ChannelKeyFingerprintTests
{
    private static string Fingerprint() =>
        SecretProtector.ComputeKeyFingerprint(RandomNumberGenerator.GetBytes(32));

    // ── Decide: the eight cases ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Case1_NoStoredFingerprint_IsFirstRun()
    {
        ChannelKeyFingerprint.Decide(storedFingerprint: null, Fingerprint(), rotationAck: null)
            .Should().Be(ChannelKeyFingerprintOutcome.FirstRun);
    }

    [Fact]
    public void Decide_Case1b_EmptyStoredFingerprint_IsFirstRun()
    {
        ChannelKeyFingerprint.Decide(storedFingerprint: "   ", Fingerprint(), rotationAck: null)
            .Should().Be(ChannelKeyFingerprintOutcome.FirstRun);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 65 non-hex chars
    public void Decide_Case2_MalformedStoredFingerprint_Throws(string malformed)
    {
        var act = () => ChannelKeyFingerprint.Decide(malformed, Fingerprint(), rotationAck: null);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decide_Case3_MatchesNoAck_IsMatched()
    {
        var current = Fingerprint();
        ChannelKeyFingerprint.Decide(current, current, rotationAck: null)
            .Should().Be(ChannelKeyFingerprintOutcome.Matched);
    }

    [Fact]
    public void Decide_Case4_MatchesWithLeftoverAck_IsMatchedWithLeftoverAck()
    {
        var current = Fingerprint();
        ChannelKeyFingerprint.Decide(current, current, rotationAck: "deadbeef")
            .Should().Be(ChannelKeyFingerprintOutcome.MatchedWithLeftoverAck);
    }

    [Fact]
    public void Decide_Case5_MismatchNoAck_Throws()
    {
        var act = () => ChannelKeyFingerprint.Decide(Fingerprint(), Fingerprint(), rotationAck: null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*NOTIFICATIONS_ENCRYPTION_KEY*");
    }

    [Fact]
    public void Decide_Case6_MismatchMalformedAck_Throws()
    {
        var act = () => ChannelKeyFingerprint.Decide(Fingerprint(), Fingerprint(), rotationAck: "not-hex-and-too-long");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decide_Case7_MismatchWrongAck_Throws()
    {
        var current = Fingerprint();
        // A plausible-looking 8-hex-char ack, but it does not name the CURRENT key's id.
        var wrongAck = current[..8] == "deadbeef" ? "beefdead" : "deadbeef";

        var act = () => ChannelKeyFingerprint.Decide(Fingerprint(), current, rotationAck: wrongAck);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decide_Case8_MismatchCorrectAck_IsRotatedByAck()
    {
        var current = Fingerprint();
        var correctAck = current[..8];

        ChannelKeyFingerprint.Decide(Fingerprint(), current, rotationAck: correctAck)
            .Should().Be(ChannelKeyFingerprintOutcome.RotatedByAck);
    }

    [Fact]
    public void Decide_Case8b_AckIsCaseInsensitive()
    {
        var current = Fingerprint();
        var correctAck = current[..8].ToUpperInvariant();

        ChannelKeyFingerprint.Decide(Fingerprint(), current, rotationAck: correctAck)
            .Should().Be(ChannelKeyFingerprintOutcome.RotatedByAck);
    }

    [Fact]
    public void Decide_StoredFingerprintComparisonIsCaseInsensitive()
    {
        var current = Fingerprint();
        ChannelKeyFingerprint.Decide(current.ToUpperInvariant(), current, rotationAck: null)
            .Should().Be(ChannelKeyFingerprintOutcome.Matched);
    }

    // ── ValidateAndPersist: IO wrapper over fake delegates ─────────────────────────────────────────

    private sealed class FakeFileSystem
    {
        public readonly Dictionary<string, string> Files = new();
        public readonly List<string> Warnings = [];

        public bool Exists(string path) => Files.ContainsKey(path);
        public string Read(string path) => Files[path];
        public void Write(string path, string content) => Files[path] = content;
        public void Warn(string message) => Warnings.Add(message);
    }

    [Fact]
    public void ValidateAndPersist_FirstRun_WritesFingerprintFile_NoWarning()
    {
        var fs = new FakeFileSystem();
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var expectedFingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);

        var outcome = ChannelKeyFingerprint.ValidateAndPersist(
            keyBytes, rotationAck: null, "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        outcome.Should().Be(ChannelKeyFingerprintOutcome.FirstRun);
        fs.Files.Should().ContainKey("fingerprint.txt");
        fs.Files["fingerprint.txt"].Should().Contain(expectedFingerprint);
        fs.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAndPersist_Matched_DoesNotRewriteFile()
    {
        var fs = new FakeFileSystem();
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var fingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);
        fs.Files["fingerprint.txt"] = fingerprint;

        ChannelKeyFingerprint.ValidateAndPersist(
            keyBytes, rotationAck: null, "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        fs.Files["fingerprint.txt"].Should().Be(fingerprint); // unchanged
        fs.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAndPersist_Mismatch_ThrowsAndDoesNotWrite()
    {
        var fs = new FakeFileSystem();
        var oldKeyBytes = RandomNumberGenerator.GetBytes(32);
        fs.Files["fingerprint.txt"] = SecretProtector.ComputeKeyFingerprint(oldKeyBytes);

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);

        var act = () => ChannelKeyFingerprint.ValidateAndPersist(
            newKeyBytes, rotationAck: null, "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        act.Should().Throw<InvalidOperationException>();
        fs.Files["fingerprint.txt"].Should().Be(SecretProtector.ComputeKeyFingerprint(oldKeyBytes)); // untouched
    }

    [Fact]
    public void ValidateAndPersist_RotatedByAck_OverwritesFileAndWarns()
    {
        var fs = new FakeFileSystem();
        var oldKeyBytes = RandomNumberGenerator.GetBytes(32);
        fs.Files["fingerprint.txt"] = SecretProtector.ComputeKeyFingerprint(oldKeyBytes);

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKeyId = SecretProtector.ComputeKeyId(newKeyBytes);

        var outcome = ChannelKeyFingerprint.ValidateAndPersist(
            newKeyBytes, rotationAck: newKeyId, "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        outcome.Should().Be(ChannelKeyFingerprintOutcome.RotatedByAck);
        fs.Files["fingerprint.txt"].Should().Contain(SecretProtector.ComputeKeyFingerprint(newKeyBytes));
        fs.Warnings.Should().ContainSingle(w => w.Contains("NOTIFICATIONS_KEY_ROTATION_ACK"));
    }

    [Fact]
    public void ValidateAndPersist_MatchedWithLeftoverAck_WarnsButDoesNotThrow()
    {
        var fs = new FakeFileSystem();
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var fingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);
        fs.Files["fingerprint.txt"] = fingerprint;

        var act = () => ChannelKeyFingerprint.ValidateAndPersist(
            keyBytes, rotationAck: "deadbeef", "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        act.Should().NotThrow();
        fs.Warnings.Should().ContainSingle(w => w.Contains("NOTIFICATIONS_KEY_ROTATION_ACK"));
    }

    [Fact]
    public void ValidateAndPersist_FileContentIgnoresCommentLines()
    {
        var fs = new FakeFileSystem();
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var fingerprint = SecretProtector.ComputeKeyFingerprint(keyBytes);
        fs.Files["fingerprint.txt"] = $"# a comment\n# another comment\n\n{fingerprint}\n";

        var act = () => ChannelKeyFingerprint.ValidateAndPersist(
            keyBytes, rotationAck: null, "fingerprint.txt",
            fs.Exists, fs.Read, fs.Write, fs.Warn);

        act.Should().NotThrow();
    }
}
