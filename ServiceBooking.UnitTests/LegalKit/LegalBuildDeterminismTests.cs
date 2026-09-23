using FluentAssertions;
using ServiceBooking.LegalKit;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>`legal build` (ARCHITECTURE_CYCLE11.md §105.1): determinism, the D9→D3 splice rule, and the
/// marker-missing failure mode (§103.2, risk A1) — all in-memory-adjacent temp directories, no host, no
/// database, no HTTP.</summary>
public class LegalBuildDeterminismTests
{
    [Fact]
    public void Build_SameSourceTwice_ProducesByteIdenticalOutput()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var out1 = Path.Combine(Path.GetTempPath(), "legalkit-out1-" + Guid.NewGuid().ToString("N"));
        var out2 = Path.Combine(Path.GetTempPath(), "legalkit-out2-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, out1);
            LegalSourceSet.Build(source, out2);

            var files1 = Directory.GetFiles(out1).Select(Path.GetFileName).OrderBy(f => f).ToList();
            var files2 = Directory.GetFiles(out2).Select(Path.GetFileName).OrderBy(f => f).ToList();
            files1.Should().BeEquivalentTo(files2);

            foreach (var file in files1)
                File.ReadAllBytes(Path.Combine(out1, file!)).Should().BeEquivalentTo(File.ReadAllBytes(Path.Combine(out2, file!)));
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(out1);
            LegalKitFixture.Delete(out2);
        }
    }

    [Fact]
    public void Build_SplicesChannelOfferBodyAfterMarker_IntoTermsOwner()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, outDir);

            var termsOwner = File.ReadAllText(Path.Combine(outDir, "terms-owner.html"));
            termsOwner.Should().Contain(LegalKitFixture.DefaultTermsOwnerHtml);
            termsOwner.Should().Contain("Offer appendix body {{НДС_ОГОВОРКА}}");
            // The header above the marker (with {{ВЕРСИЯ_ДОКУМЕНТА}}) must NOT leak into the artifact —
            // only the body below APPENDIX-BODY-START does (§103.2).
            termsOwner.Should().NotContain("header");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Build_ChannelOfferMissingMarker_ThrowsClearError()
    {
        var source = LegalKitFixture.CreateSourceDir(includeMarker: false);
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var act = () => LegalSourceSet.Build(source, outDir);
            act.Should().Throw<InvalidOperationException>().WithMessage("*APPENDIX-BODY-START*");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Build_MissingChannelOfferFile_ThrowsClearError()
    {
        var source = LegalKitFixture.CreateSourceDir();
        File.Delete(Path.Combine(source, "09-channel-offer.html"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var act = () => LegalSourceSet.Build(source, outDir);
            act.Should().Throw<InvalidOperationException>().WithMessage("*09-channel-offer.html*");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }

    [Fact]
    public void Build_ResultLoadsSuccessfullyViaLoadStrict()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, outDir);

            // The same loader the product uses — a build that LoadStrict rejects is, by definition, an
            // invalid artifact (ARCHITECTURE_CYCLE11.md §105.1).
            var act = () => LegalProviderFactory.CreateForRoot(outDir).LoadStrict();
            act.Should().NotThrow();
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(outDir);
        }
    }
}
