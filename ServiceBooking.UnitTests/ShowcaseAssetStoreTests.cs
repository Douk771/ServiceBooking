using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §575.6 — manifest reading, idempotent copies into <c>uploads/showcase/</c>, skipping what is missing. Files only, no database.</summary>
public sealed class ShowcaseAssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sb-showcase-" + Guid.NewGuid().ToString("N"));
    private readonly string _assets;
    private readonly string _uploads;
    private readonly FileStorage _storage;
    private readonly ShowcaseAssetStore _store;

    public ShowcaseAssetStoreTests()
    {
        _assets = Path.Combine(_root, "ShowcaseAssets");
        _uploads = Path.Combine(_root, "uploads");
        Directory.CreateDirectory(_assets);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:PublicRoot"] = _uploads, ["Storage:PrivateRoot"] = Path.Combine(_root, "private"),
        }).Build();
        var env = new FakeEnv { ContentRootPath = _root };
        _storage = new FileStorage(config, env);
        _store = new ShowcaseAssetStore(_storage, env, NullLogger<ShowcaseAssetStore>.Instance);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_assets, "manifest.json"), json);

    [Fact]
    public void NoManifest_MeansNothingIsAvailable_NoException()
    {
        _store.Manifest.Should().BeEmpty();
        _store.IsAvailable("logo.beauty").Should().BeFalse();
        _store.CountFiles(["logo.beauty"]).Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_CopiesTheFileUnderItsContentHash_AndIsIdempotent()
    {
        File.WriteAllBytes(Path.Combine(_assets, "a.jpg"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(_assets, "a.thumb.jpg"), [9, 9]);
        WriteManifest("""{ "version": 1, "assets": [ { "key": "logo.beauty", "role": "logo", "file": "a.jpg", "thumbnail": "a.thumb.jpg", "width": 10, "height": 20 } ] }""");

        var first = await _store.PublishAsync("logo.beauty", CancellationToken.None);
        var second = await _store.PublishAsync("logo.beauty", CancellationToken.None);

        first.Should().NotBeNull();
        first!.Url.Should().StartWith("/uploads/showcase/").And.EndWith(".jpg");
        first.ThumbnailUrl.Should().StartWith("/uploads/showcase/");
        first.SizeBytes.Should().Be(4);
        first.Width.Should().Be(10);
        first.ContentType.Should().Be("image/jpeg");
        second.Should().BeSameAs(first);
        Directory.GetFiles(Path.Combine(_uploads, "showcase")).Should().HaveCount(2);

        // A second store instance (a second run of the command) writes the same names: content-addressed, nothing piles up.
        var again = new ShowcaseAssetStore(_storage, new FakeEnv { ContentRootPath = _root }, NullLogger<ShowcaseAssetStore>.Instance);
        (await again.PublishAsync("logo.beauty", CancellationToken.None))!.Url.Should().Be(first.Url);
        Directory.GetFiles(Path.Combine(_uploads, "showcase")).Should().HaveCount(2);
    }

    [Fact]
    public async Task PublishAsync_SkipsAnAssetNotInTheManifest_AndOneWhoseFileIsMissing_WithoutDrawingAnything()
    {
        WriteManifest("""{ "version": 1, "assets": [ { "key": "photo.x.1", "role": "photo", "file": "missing.jpg", "width": 1, "height": 1 } ] }""");

        (await _store.PublishAsync("photo.x.1", CancellationToken.None)).Should().BeNull();
        (await _store.PublishAsync("nope", CancellationToken.None)).Should().BeNull();
        _store.IsAvailable("photo.x.1").Should().BeFalse();
        Directory.Exists(Path.Combine(_uploads, "showcase")).Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_RefusesAPathThatLeavesTheAssetsDirectory()
    {
        File.WriteAllBytes(Path.Combine(_root, "secret.jpg"), [7]);
        WriteManifest("""{ "version": 1, "assets": [ { "key": "logo.x", "role": "logo", "file": "../secret.jpg", "width": 1, "height": 1 } ] }""");

        (await _store.PublishAsync("logo.x", CancellationToken.None)).Should().BeNull();
        _store.IsAvailable("logo.x").Should().BeFalse();
    }

    [Fact]
    public void CountFiles_CountsPictureAndThumbnail_DistinctFilesOnce()
    {
        File.WriteAllBytes(Path.Combine(_assets, "a.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(_assets, "a.thumb.jpg"), [2]);
        File.WriteAllBytes(Path.Combine(_assets, "b.webp"), [3]);
        WriteManifest("""
            { "version": 1, "assets": [
              { "key": "k1", "role": "photo", "file": "a.jpg", "thumbnail": "a.thumb.jpg", "width": 1, "height": 1 },
              { "key": "k2", "role": "photo", "file": "a.jpg", "thumbnail": "a.thumb.jpg", "width": 1, "height": 1 },
              { "key": "k3", "role": "service", "file": "b.webp", "width": 1, "height": 1 } ] }
            """);

        _store.CountFiles(["k1", "k2", "k3", "absent"]).Should().Be(3);
    }

    [Fact]
    public void FileStorage_ShowcaseArea_IsItsOwnFolder_AndCanBeRemovedWhole()
    {
        _storage.SavePublicNamedAsync(PublicArea.Showcase, "x.jpg", [1]).GetAwaiter().GetResult().Should().Be("/uploads/showcase/x.jpg");
        _storage.CountPublicAreaFiles(PublicArea.Showcase).Should().Be(1);

        _storage.DeletePublicAreaFolder(PublicArea.Showcase);

        _storage.CountPublicAreaFiles(PublicArea.Showcase).Should().Be(0);
        Directory.Exists(Path.Combine(_uploads, "showcase")).Should().BeFalse();
    }

    [Fact]
    public async Task Recreate_CleanupKeepsTheJustPublishedPictures_AndRemovesOnlyTheOldOnes()
    {
        // The review finding: recreate publishes into uploads/showcase/ and afterwards used to wipe the whole folder, leaving every new row with a broken picture.
        File.WriteAllBytes(Path.Combine(_assets, "a.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_assets, "a.thumb.jpg"), [4]);
        WriteManifest("""{ "version": 1, "assets": [ { "key": "logo.x", "role": "logo", "file": "a.jpg", "thumbnail": "a.thumb.jpg", "width": 1, "height": 1 } ] }""");
        await _storage.SavePublicNamedAsync(PublicArea.Showcase, "old-orphan.jpg", [9]);
        var published = (await _store.PublishAsync("logo.x", CancellationToken.None))!;
        var eraser = new ShowcaseEraser(null!, _storage, NullLogger<ShowcaseEraser>.Instance);

        eraser.DeleteFilesAfterCommit([], _store.PublishedFileNames);

        var left = Directory.GetFiles(Path.Combine(_uploads, "showcase")).Select(Path.GetFileName).ToList();
        left.Should().BeEquivalentTo(new[] { Path.GetFileName(published.Url), Path.GetFileName(published.ThumbnailUrl!) });
    }

    [Fact]
    public async Task PlainDelete_CleanupRemovesTheWholeShowcaseFolder()
    {
        await _storage.SavePublicNamedAsync(PublicArea.Showcase, "x.jpg", [1]);
        var eraser = new ShowcaseEraser(null!, _storage, NullLogger<ShowcaseEraser>.Instance);

        eraser.DeleteFilesAfterCommit([]);

        Directory.Exists(Path.Combine(_uploads, "showcase")).Should().BeFalse();
    }

    [Fact]
    public void PublishedFileNames_IsEmptyBeforeAnythingIsPublished() => _store.PublishedFileNames.Should().BeEmpty();

    [Fact]
    public async Task FileStorage_DeleteAreaFolderExcept_KeepsNamedFiles_AndDropsTheFolderWhenNothingIsLeft()
    {
        await _storage.SavePublicNamedAsync(PublicArea.Showcase, "keep.jpg", [1]);
        await _storage.SavePublicNamedAsync(PublicArea.Showcase, "drop.jpg", [2]);

        _storage.DeletePublicAreaFolderExcept(PublicArea.Showcase, new HashSet<string> { "keep.jpg" });
        Directory.GetFiles(Path.Combine(_uploads, "showcase")).Select(Path.GetFileName).Should().Equal("keep.jpg");

        _storage.DeletePublicAreaFolderExcept(PublicArea.Showcase, new HashSet<string>());
        Directory.Exists(Path.Combine(_uploads, "showcase")).Should().BeFalse();
    }

    [Theory]
    [InlineData("../x.jpg")]
    [InlineData("a/b.jpg")]
    [InlineData("")]
    public async Task FileStorage_SavePublicNamed_RefusesAnythingButAPlainFileName(string name) =>
        await FluentActions.Invoking(() => _storage.SavePublicNamedAsync(PublicArea.Showcase, name, [1])).Should().ThrowAsync<ArgumentException>();

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "t";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
