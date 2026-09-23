using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Constructs a bare <see cref="LegalDocumentProvider"/> outside of the API's DI container/host
/// (ARCHITECTURE_CYCLE11.md §104.1 — this tool never builds the API's host). Every command that reads a
/// manifest gives an explicit <c>--root</c>/<c>--source</c>, so <see cref="LegalOptions.Root"/> is always
/// set and <see cref="IWebHostEnvironment.ContentRootPath"/> is never actually consulted; the stub below
/// exists purely to satisfy the constructor's signature, not because this tool has a content root.
/// </summary>
internal static class LegalProviderFactory
{
    public static LegalDocumentProvider CreateForRoot(string root)
    {
        var options = Options.Create(new LegalOptions { Root = root });
        return new LegalDocumentProvider(options, new NullWebHostEnvironment(), NullLogger<LegalDocumentProvider>.Instance);
    }

    private sealed class NullWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "LegalKit";
        public string ApplicationName { get; set; } = "ServiceBooking.LegalKit";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
