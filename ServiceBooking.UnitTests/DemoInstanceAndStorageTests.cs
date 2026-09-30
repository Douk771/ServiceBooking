using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.2 lock 2, §580 step 4, §579.4 — the start-time decision about a database, the demo reset's wipe of the file storage, and the demo
/// claim of the token. Filesystem and in-memory only: no database, no server.
/// </summary>
public class DemoInstanceAndStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sb-demo-storage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ── DemoInstanceGuard.Decide ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("demo", false, false, DemoInstanceDecision.Ok)]
    [InlineData("demo", true, true, DemoInstanceDecision.Ok)]          // a visitor's own company or booking on a marked demo is normal
    [InlineData("production", false, false, DemoInstanceDecision.RefuseWrongKind)]
    [InlineData("", false, false, DemoInstanceDecision.RefuseWrongKind)]
    [InlineData("Demo", false, false, DemoInstanceDecision.RefuseWrongKind)] // exact value only
    [InlineData(null, false, false, DemoInstanceDecision.WriteMark)]   // a fresh empty database
    [InlineData(null, true, false, DemoInstanceDecision.RefuseRealData)]
    [InlineData(null, false, true, DemoInstanceDecision.RefuseRealData)]
    [InlineData(null, true, true, DemoInstanceDecision.RefuseRealData)]
    public void Decide_FollowsTheMarkAndTheContent(string? kind, bool unmarkedCompany, bool unmarkedBooking, DemoInstanceDecision expected) =>
        DemoInstanceGuard.Decide(kind, unmarkedCompany, unmarkedBooking).Should().Be(expected);

    [Fact]
    public void TheRealDataMessage_IsTheOneTheArchitectureNames() =>
        DemoInstanceGuard.RealDataMessage.Should().Contain("БД содержит настоящие данные — это не демо-БД");

    [Fact]
    public void TheInstanceKeyIsTheSharedShowcaseConstant() =>
        DemoCatalog.InstanceKindKey.Should().Be("instance.kind");

    // ── FileStorage.ClearAllFiles ───────────────────────────────────────────────────────────────────

    private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "t";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private (FileStorage Storage, string PublicRoot, string PrivateRoot) NewStorage()
    {
        var content = Path.Combine(_root, "app");
        var publicRoot = Path.Combine(_root, "vol", "uploads");
        var privateRoot = Path.Combine(_root, "vol", "private");
        Directory.CreateDirectory(content);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:PublicRoot"] = publicRoot,
            ["Storage:PrivateRoot"] = privateRoot,
        }).Build();
        return (new FileStorage(config, new FakeEnvironment(content)), publicRoot, privateRoot);
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void ClearAllFiles_DeletesEverything_ExceptTheShowcasePicturesTheNewDataPointsAt()
    {
        var (storage, publicRoot, privateRoot) = NewStorage();
        Touch(Path.Combine(publicRoot, "showcase", "keep-1.jpg"));
        Touch(Path.Combine(publicRoot, "showcase", "keep-1-thumb.jpg"));
        Touch(Path.Combine(publicRoot, "showcase", "old-showcase.jpg"));   // not named by the new data
        Touch(Path.Combine(publicRoot, "companies", "visitor-logo.jpg"));  // a visitor's upload
        Touch(Path.Combine(publicRoot, "avatars", "a", "deep.jpg"));
        Touch(Path.Combine(publicRoot, "keep-1.jpg"));                     // same NAME, but not inside showcase/: still deleted
        Touch(Path.Combine(privateRoot, "c1", "note-photo.enc"));
        Touch(Path.Combine(privateRoot, "top.enc"));

        var deleted = storage.ClearAllFiles(new HashSet<string> { "keep-1.jpg", "keep-1-thumb.jpg" });

        deleted.Should().Be(6);
        Directory.EnumerateFiles(publicRoot, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(publicRoot, f)).Should()
            .BeEquivalentTo([Path.Combine("showcase", "keep-1.jpg"), Path.Combine("showcase", "keep-1-thumb.jpg")]);
        Directory.EnumerateFiles(privateRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.Exists(publicRoot).Should().BeTrue("the root itself stays: it is a volume");
        Directory.Exists(privateRoot).Should().BeTrue();
        Directory.Exists(Path.Combine(publicRoot, "companies")).Should().BeFalse("folders left empty are removed");
        Directory.Exists(Path.Combine(privateRoot, "c1")).Should().BeFalse();
    }

    [Fact]
    public void ClearAllFiles_OnRootsThatDoNotExistYet_IsANoOp()
    {
        var (storage, _, _) = NewStorage();

        storage.ClearAllFiles(new HashSet<string>()).Should().Be(0);
    }

    [Theory]
    [InlineData("/app", "/app", false)]                   // the content root itself
    [InlineData("/app", "/app/wwwroot", false)]           // an ancestor of the content root (a mistyped volume would delete the application)
    [InlineData("/", "/app", false)]
    [InlineData("/data", "/app", false)]                  // a single segment below the filesystem root: too close
    [InlineData("/data/uploads", "/app", true)]
    [InlineData("/app/wwwroot/uploads", "/app", true)]    // the default: inside the content root, below it
    [InlineData("/app/App_Data/private-uploads", "/app", true)]
    [InlineData("/app/", "/app", false)]
    public void IsSafeToClear_RefusesTheApplicationItselfAndTheFilesystemRoot(string root, string contentRoot, bool expected) =>
        FileStorage.IsSafeToClear(root, contentRoot).Should().Be(expected);

    [Fact]
    public void ClearAllFiles_RefusesAnUnsafeRoot_AndDeletesNothing()
    {
        var content = Path.Combine(_root, "app");
        Touch(Path.Combine(content, "appsettings.json"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:PublicRoot"] = content, // the mistake: the public root IS the application folder
            ["Storage:PrivateRoot"] = Path.Combine(_root, "vol", "private"),
        }).Build();
        var storage = new FileStorage(config, new FakeEnvironment(content));

        var act = () => storage.ClearAllFiles(new HashSet<string>());

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Refusing");
        File.Exists(Path.Combine(content, "appsettings.json")).Should().BeTrue();
    }

    // ── the demo claim of the token ─────────────────────────────────────────────────────────────────

    private static string Claim(JwtSecurityToken token, string type) => token.Claims.FirstOrDefault(c => c.Type == type)?.Value ?? "";

    [Fact]
    public void Token_CarriesTheDemoClaim_OnlyWhenAskedTo()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "A_KEY_FOR_THIS_TEST_THAT_IS_AT_LEAST_32_CHARACTERS",
            ["Jwt:Issuer"] = "ServiceBooking.Demo",
            ["Jwt:Audience"] = "ServiceBookingClient.Demo",
        }).Build();
        var service = new TokenService(config);
        var user = new AppUser { Id = "u1", FirstName = "А", LastName = "Б", PhoneNumber = "72005550000", SecurityStamp = "s" };
        var handler = new JwtSecurityTokenHandler();

        var demo = handler.ReadJwtToken(service.GenerateToken(user, ["CompanyOwner"], "p1", "t1", "o1", demo: true));
        var ordinary = handler.ReadJwtToken(service.GenerateToken(user, ["CompanyOwner"], "p1", "t1", "o1"));

        Claim(demo, DemoForbiddenFilter.ClaimType).Should().Be("1");
        Claim(ordinary, DemoForbiddenFilter.ClaimType).Should().BeEmpty("a real account's token never carries it");
        demo.Issuer.Should().Be("ServiceBooking.Demo");
        Claim(demo, "lcp").Should().Be("p1");
        Claim(demo, "lct").Should().Be("t1");
        Claim(demo, "lco").Should().Be("o1");
    }
}
