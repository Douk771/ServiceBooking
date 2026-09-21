using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// 60-second in-memory cache in front of <see cref="PricingCatalogBuilder"/>
/// (ARCHITECTURE_CYCLE5.md §48) — the public price list is read on every homepage/pricing-page hit and
/// must never cost a query per anonymous visitor.
/// </summary>
public sealed class PricingCatalogCache(AppDbContext db, IMemoryCache cache)
{
    public const string PublicEnabledSettingKey = "pricing.public-enabled";

    private const string CacheKey = "pricing:public";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions HashSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record CachedPricing(PublicPricingDto Dto, string ETag);

    /// <summary>Whole built payload plus its ETag, or <see langword="null"/> when there is nothing
    /// public to publish yet — the plan/option tables are empty. The 404-vs-empty-list decision for
    /// the disabled-publication case is the caller's (§48: 404, never an empty array).</summary>
    public async Task<CachedPricing> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out CachedPricing? cached) && cached is not null) return cached;

        var plans = await db.SubscriptionPlanConfigs.AsNoTracking().ToListAsync(ct);
        var options = await db.SubscriptionOptions.AsNoTracking().ToListAsync(ct);
        var legalNotice = await GetRawSettingAsync("pricing.legal-notice", ct);

        var dto = PricingCatalogBuilder.Build(version: "pending", plans, options, legalNotice);
        var etag = ComputeETag(dto);
        // version mirrors the ETag per contract ("Совпадает со значением внутри ETag") — rebuild once
        // with the real value rather than trying to compute the hash and embed it in the same payload.
        dto = dto with { Version = etag };
        var result = new CachedPricing(dto, $"W/\"{etag}\"");

        cache.Set(CacheKey, result, CacheDuration);
        return result;
    }

    /// <summary>Rubильник публикации (§48) — absent key means disabled, matching every other
    /// PlatformSetting boolean flag convention in this codebase (absence, never a false default row).</summary>
    public async Task<bool> IsPublicEnabledAsync(CancellationToken ct = default)
    {
        var raw = await GetRawSettingAsync(PublicEnabledSettingKey, ct);
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Called right after any admin write to a plan, option, rule or the relevant
    /// PlatformSetting keys — same convention as <c>PlatformSettings.InvalidateCache</c> (cycle 4). No
    /// admin write endpoint exists yet in this slice (see the backend report for cycle 07); wired up
    /// here so that slice only has to call it, not re-discover the cache key.</summary>
    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<string?> GetRawSettingAsync(string key, CancellationToken ct) =>
        await db.PlatformSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

    private static string ComputeETag(PublicPricingDto dto)
    {
        var json = JsonSerializer.Serialize(dto, HashSerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
