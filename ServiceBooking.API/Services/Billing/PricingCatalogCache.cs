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
    private const string PublicEnabledCacheKey = "pricing:public-enabled";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions HashSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record CachedPricing(PublicPricingDto Dto, string ETag);

    /// <summary>Whole built payload plus its ETag, or <see langword="null"/> when there is nothing
    /// public to publish yet — the plan/option tables are empty. The 404-vs-empty-list decision for
    /// the disabled-publication case is the caller's (§48: 404, never an empty array).</summary>
    public async Task<CachedPricing> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out CachedPricing? cached) && cached is not null) return cached;

        var result = await BuildAsync(ct);
        cache.Set(CacheKey, result, CacheDuration);
        return result;
    }

    /// <summary>Same payload as <see cref="GetAsync"/> but built straight from the database, bypassing
    /// (and never populating) the 60-second cache — API_CONTRACT_CYCLE5.md §40: the admin preview must
    /// show a just-saved change immediately, not up to a minute later.</summary>
    public Task<CachedPricing> BuildFreshAsync(CancellationToken ct = default) => BuildAsync(ct);

    private async Task<CachedPricing> BuildAsync(CancellationToken ct)
    {
        var plans = await db.SubscriptionPlanConfigs.AsNoTracking().ToListAsync(ct);
        var options = await db.SubscriptionOptions.AsNoTracking().ToListAsync(ct);
        var legalNotice = await GetRawSettingAsync("pricing.legal-notice", ct);

        var dto = PricingCatalogBuilder.Build(version: "pending", plans, options, legalNotice);
        var etag = ComputeETag(dto);
        // version mirrors the ETag per contract ("Совпадает со значением внутри ETag") — rebuild once
        // with the real value rather than trying to compute the hash and embed it in the same payload.
        dto = dto with { Version = etag };
        return new CachedPricing(dto, $"W/\"{etag}\"");
    }

    /// <summary>Rubильник публикации (§48) — absent key means disabled, matching every other
    /// PlatformSetting boolean flag convention in this codebase (absence, never a false default row).
    /// Cached alongside the catalog itself: ARCHITECTURE_CYCLE5.md §48 explicitly drops rate limiting
    /// for GET /api/pricing on the premise the whole request is served from memory — a per-request
    /// database round trip just for this flag would make that premise false.</summary>
    public async Task<bool> IsPublicEnabledAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(PublicEnabledCacheKey, out bool cached)) return cached;

        var raw = await GetRawSettingAsync(PublicEnabledSettingKey, ct);
        var enabled = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        cache.Set(PublicEnabledCacheKey, enabled, CacheDuration);
        return enabled;
    }

    /// <summary>Wired into <c>AdminController</c>'s plan create/update/delete endpoints, so a plan
    /// change is reflected immediately rather than after the 60-second TTL. There is currently no admin
    /// endpoint that writes the pricing.public-enabled / pricing.legal-notice PlatformSetting rows
    /// (that toggle is still set directly in the database — ARCHITECTURE_CYCLE5.md §48 slice not yet
    /// built), so flipping either of those keys is NOT covered by this call and only takes effect once
    /// <see cref="PublicEnabledCacheKey"/> naturally expires (up to 60s). Once an endpoint for those
    /// keys exists it must call this too.</summary>
    public void Invalidate()
    {
        cache.Remove(CacheKey);
        cache.Remove(PublicEnabledCacheKey);
    }

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
