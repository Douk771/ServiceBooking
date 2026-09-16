using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Regression coverage for the path-traversal finding: <c>DeletePublic</c> used to trust the
/// "/uploads/" prefix alone and combine+delete without verifying the result stayed inside the public
/// root, unlike <c>OpenPrivate</c>/<c>DeletePrivate</c>, which always checked. No DB, no network — just
/// a temp directory standing in for wwwroot/uploads, so this is a real unit test despite exercising the
/// filesystem.
/// </summary>
public class FileStorageTests : IDisposable
{
    private readonly string _publicRoot;
    private readonly string _privateRoot;
    private readonly string _canaryOutsideRoot;
    private readonly FileStorage _storage;

    public FileStorageTests()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sb-filestorage-tests-" + Guid.NewGuid());
        _publicRoot = Path.Combine(sandbox, "public");
        _privateRoot = Path.Combine(sandbox, "private");
        Directory.CreateDirectory(_publicRoot);
        Directory.CreateDirectory(_privateRoot);

        // Sits as a SIBLING of the public root — exactly what "/uploads/../secret.txt"-style traversal
        // would reach if DeletePublic didn't check containment.
        _canaryOutsideRoot = Path.Combine(sandbox, "secret.txt");
        File.WriteAllText(_canaryOutsideRoot, "do not delete me");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:PublicRoot"] = _publicRoot,
                ["Storage:PrivateRoot"] = _privateRoot,
            })
            .Build();

        _storage = new FileStorage(config, new FakeWebHostEnvironment());
    }

    public void Dispose()
    {
        var sandbox = Path.GetDirectoryName(_publicRoot)!;
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [Fact]
    public void DeletePublic_PathTraversalViaDotDotSegments_DoesNotDeleteFileOutsideTheRoot()
    {
        var attackUrl = "/uploads/../secret.txt";

        _storage.DeletePublic(attackUrl);

        File.Exists(_canaryOutsideRoot).Should().BeTrue("DeletePublic must never touch anything outside the public root");
    }

    [Fact]
    public void DeletePublic_DeeplyNestedPathTraversal_DoesNotDeleteFileOutsideTheRoot()
    {
        // The shape from the actual finding: a client-controlled ImageUrl pointing several levels up.
        var attackUrl = "/uploads/companies/../../../secret.txt";

        _storage.DeletePublic(attackUrl);

        File.Exists(_canaryOutsideRoot).Should().BeTrue();
    }

    [Fact]
    public void DeletePublic_ValidUrlInsideRoot_StillDeletesTheFile()
    {
        var folder = Path.Combine(_publicRoot, "services");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "real-photo.jpg");
        File.WriteAllText(filePath, "pretend jpeg bytes");

        _storage.DeletePublic("/uploads/services/real-photo.jpg");

        File.Exists(filePath).Should().BeFalse("a legitimate URL inside the public root must still delete normally");
    }

    [Fact]
    public void DeletePublic_UnknownPrefix_IsANoOp()
    {
        // Doesn't start with "/uploads/" at all — e.g. a legacy external logo URL.
        var act = () => _storage.DeletePublic("https://cdn.example.com/logo.png");
        act.Should().NotThrow();
    }

    // ── ResolvePublicRoot / ResolvePrivateRoot: the shared default-resolution helpers ─────────────────
    //
    // DeploymentSafetyChecks.ValidateSecrets calls these same two static methods (sanitation cycle,
    // review round 2) instead of keeping its own inline copy of "default to {contentRoot}/wwwroot/uploads
    // / {contentRoot}/App_Data/private-uploads". Pinning their defaults here means a future edit to
    // either default only has to change one place — and if someone edits it anyway and the two callers'
    // behavior were ever to diverge, it would only be because one of them stopped calling this helper,
    // which would show up as a diff on this class, not as a silent split.

    [Fact]
    public void ResolvePublicRoot_Unconfigured_DefaultsToWwwrootUploadsUnderContentRoot()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        FileStorage.ResolvePublicRoot(config, "/srv/app")
            .Should().Be(Path.Combine("/srv/app", "wwwroot", "uploads"));
    }

    [Fact]
    public void ResolvePublicRoot_Configured_ReturnsConfiguredValueVerbatim()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:PublicRoot"] = "/srv/uploads" })
            .Build();

        FileStorage.ResolvePublicRoot(config, "/srv/app").Should().Be("/srv/uploads");
    }

    [Fact]
    public void ResolvePrivateRoot_Unconfigured_DefaultsToAppDataPrivateUploadsUnderContentRoot()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        FileStorage.ResolvePrivateRoot(config, "/srv/app")
            .Should().Be(Path.Combine("/srv/app", "App_Data", "private-uploads"));
    }

    [Fact]
    public void ResolvePrivateRoot_Configured_ReturnsConfiguredValueVerbatim()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:PrivateRoot"] = "/srv/private" })
            .Build();

        FileStorage.ResolvePrivateRoot(config, "/srv/app").Should().Be("/srv/private");
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ServiceBooking.UnitTests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
