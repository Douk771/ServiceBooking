using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.3.</summary>
public class House
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Capacity { get; set; } = 1;
    public bool ExtraBedsEnabled { get; set; }
    public int ExtraBedsMax { get; set; }
    public int ExtraBedPriceRub { get; set; }
    public bool DogsForbidden { get; set; }
    public bool HasCot { get; set; }
    public long AmenitiesMask { get; set; }
    public string? Address { get; set; }
    public string? YandexMapsUrl { get; set; }
    public string? TwoGisUrl { get; set; }
    public string? CheckInInfoText { get; set; }
    public HousePriceMode PriceMode { get; set; } = HousePriceMode.Constant;
    public int? ConstantPriceRub { get; set; }
    public HouseObjectKind? ObjectKind { get; set; }
    public string? RegistryNumber { get; set; }
    public string? RegistryUrl { get; set; }
    public bool IsPublished { get; set; }
    public int Position { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public List<HousePhoto> Photos { get; set; } = [];
}

public class HousePhoto
{
    public Guid Id { get; set; }
    public Guid HouseId { get; set; }
    public Guid CompanyId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public House House { get; set; } = null!;
}

/// <summary>A price period; <see cref="EndDate"/> is INCLUSIVE.</summary>
public class HousePricePeriod
{
    public Guid Id { get; set; }
    public Guid HouseId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int PriceRub { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>ЮР-2: append-only proof of the owner's assurance at publication. Never deleted.</summary>
public class HouseRegistryAttestation
{
    public Guid Id { get; set; }
    public Guid HouseId { get; set; }
    public Guid CompanyId { get; set; }
    public HouseObjectKind ObjectKind { get; set; }
    public string? RegistryNumber { get; set; }
    public string? RegistryUrl { get; set; }
    public string NoticeVersion { get; set; } = string.Empty;
    public DateTime AttestedAtUtc { get; set; } = DateTime.UtcNow;
    public string AttestedByUserId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}
