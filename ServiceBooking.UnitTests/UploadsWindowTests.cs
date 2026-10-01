using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Startup;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle 36: <c>Uploads:WindowMinutes</c> is a test-only substitution point; the default of the "uploads" limit window stays one minute and no shipped config sets it.</summary>
public class UploadsWindowTests
{
    [Fact]
    public void Window_WithoutTheSetting_IsOneMinute() =>
        RateLimitingExtensions.UploadsWindow(new ConfigurationBuilder().Build()).Should().Be(TimeSpan.FromMinutes(1));

    [Theory]
    [InlineData("60", 60)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    public void Window_FromTheSetting_IsTakenButNeverBelowOneMinute(string value, int expectedMinutes)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:WindowMinutes"] = value }).Build();
        RateLimitingExtensions.UploadsWindow(config).Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    [InlineData("appsettings.Testing.json")]
    public void ShippedConfigs_DoNotSetTheWindow(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ServiceBooking.API", file);
        if (!File.Exists(path)) return; // not every environment file exists
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.TryGetProperty("Uploads", out var uploads))
            uploads.TryGetProperty("WindowMinutes", out _).Should().BeFalse(file);
    }
}
