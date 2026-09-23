using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// 60-second in-memory cache in front of <see cref="PricingCatalogBuilder"/>
/// (ARCHITECTURE_CYCLE7.md §48) — the public price list is read on every homepage/pricing-page hit and
/// must never cost a query per anonymous visitor.
///
/// Depends on <see cref="LegalDocumentProvider"/> (a singleton, in-memory snapshot, no DB) as of
/// ARCHITECTURE_CYCLE11.md §102.7/§102.10 (Q7/Q11): the public catalog can't be shown at all while the
/// channel offer (TermsOwner) is a draft, and one option is dropped from it while its own legal
/// precondition isn't met. The hot path doesn't get more expensive — it's a read of an already-loaded
/// in-memory field, not a new query.
/// </summary>
public sealed class PricingCatalogCache(AppDbContext db, IMemoryCache cache, LegalDocumentProvider legalDocuments)
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
    /// (and never populating) the 60-second cache — API_CONTRACT_CYCLE7.md §40: the admin preview must
    /// show a just-saved change immediately, not up to a minute later.</summary>
    public Task<CachedPricing> BuildFreshAsync(CancellationToken ct = default) => BuildAsync(ct);

    private async Task<CachedPricing> BuildAsync(CancellationToken ct)
    {
        var plans = await db.SubscriptionPlanConfigs.AsNoTracking().ToListAsync(ct);
        var options = await db.SubscriptionOptions.AsNoTracking().ToListAsync(ct);
        var legalNotice = await GetRawSettingAsync("pricing.legal-notice", ct);

        var dto = PricingCatalogBuilder.Build(version: "pending", plans, options, legalNotice, legalSnapshot: legalDocuments.Current);
        var etag = ComputeETag(dto);
        // version mirrors the ETag per contract ("Совпадает со значением внутри ETag") — rebuild once
        // with the real value rather than trying to compute the hash and embed it in the same payload.
        dto = dto with { Version = etag };
        return new CachedPricing(dto, $"W/\"{etag}\"");
    }

    /// <summary>Rubильник публикации (§48) — absent key means disabled, matching every other
    /// PlatformSetting boolean flag convention in this codebase (absence, never a false default row).
    /// Cached alongside the catalog itself: ARCHITECTURE_CYCLE7.md §48 explicitly drops rate limiting
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

    /// <summary>The switch (<see cref="IsPublicEnabledAsync"/>) AND the legal precondition
    /// (ARCHITECTURE_CYCLE11.md §102.7, Q7): the storefront is visible only when the operator turned it
    /// on AND the channel offer (TermsOwner) is published. This is what <c>GET /api/pricing</c> must
    /// check — an operator flipping the switch on while the offer is still a draft must not make the
    /// catalog appear.</summary>
    public async Task<bool> IsCatalogPubliclyVisibleAsync(CancellationToken ct = default) =>
        await IsPublicEnabledAsync(ct) && GetPublicationBlockReason(legalDocuments.Current) is null;

    /// <summary>Why the switch can't (yet) be turned on, or null if there's no legal obstacle right now
    /// (ARCHITECTURE_CYCLE11.md §114.1/§114.2). Shared by the informational field on
    /// <c>GET /api/admin/platform-settings</c> and by the 409 check on its <c>PUT</c> — one rule, read
    /// twice, never re-derived.</summary>
    public static string? GetPublicationBlockReason(LegalSnapshot? snapshot)
    {
        if (snapshot is null) return "LegalUnavailable";
        var offer = snapshot.Get(LegalDocumentType.TermsOwner);
        if (offer is null) return "LegalUnavailable";
        return offer.IsDraft ? "OfferIsDraft" : null;
    }

    /// <summary>Wired into <c>AdminController</c>'s plan create/update/delete endpoints, so a plan
    /// change is reflected immediately rather than after the 60-second TTL. Also called by
    /// <c>PUT /api/admin/platform-settings</c> whenever <c>pricingPublicEnabled</c> actually changes
    /// (cycle-07), so that toggle no longer needs to wait out the up-to-60s <see cref="PublicEnabledCacheKey"/>
    /// TTL either. pricing.legal-notice has no admin write endpoint yet and is still set directly in the
    /// database; once one exists it must call this too.</summary>
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
