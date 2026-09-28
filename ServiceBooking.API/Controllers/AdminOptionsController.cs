using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the options-catalog routes of the former
/// <c>AdminBillingController</c> — same <c>api/admin</c> prefix, same SuperAdmin gate, same per-action routes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminOptionsController(AppDbContext db, PricingCatalogCache pricingCatalogCache) : ControllerBase
{
    // ── Options catalog (US-66) ───────────────────────────────────────────────────

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(CancellationToken ct)
    {
        var options = await db.SubscriptionOptions.OrderBy(o => o.SortOrder).ThenBy(o => o.Name).ToListAsync(ct);
        var counts = await GetOptionSubscriberCountsAsync(options.Select(o => o.Id));
        return Ok(new { options = options.Select(o => MapOptionDto(o, counts.GetValueOrDefault(o.Id))).ToList() });
    }

    [HttpPost("options")]
    public async Task<IActionResult> CreateOption([FromBody] AdminOptionInput dto)
    {
        var validationError = ValidateOptionInput(dto);
        if (validationError is not null) return validationError;

        if (await db.SubscriptionOptions.AnyAsync(o => o.Code == dto.Code))
            return Conflict($"Опция с кодом «{dto.Code}» уже существует.");

        var option = new SubscriptionOption
        {
            Id = Guid.NewGuid(),
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description,
            Kind = ParseKind(dto.Kind),
            CapabilityKey = dto.CapabilityKey,
            PricePerMonth = dto.PricePerMonth,
            UnitName = dto.UnitName,
            MaxQuantity = dto.MaxQuantity,
            IsPublic = dto.IsPublic,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
        };
        db.SubscriptionOptions.Add(option);
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return StatusCode(StatusCodes.Status201Created, MapOptionDto(option, 0));
    }

    [HttpPut("options/{id:guid}")]
    public async Task<IActionResult> UpdateOption(Guid id, [FromBody] AdminOptionInput dto)
    {
        var option = await db.SubscriptionOptions.FindAsync(id);
        if (option is null) return NotFound();

        if (dto.Code != option.Code)
            return BadRequest("Код опции менять нельзя.");

        var validationError = ValidateOptionInput(dto);
        if (validationError is not null) return validationError;

        option.Name = dto.Name;
        option.Description = dto.Description;
        option.Kind = ParseKind(dto.Kind);
        option.CapabilityKey = dto.CapabilityKey;
        option.PricePerMonth = dto.PricePerMonth;
        option.UnitName = dto.UnitName;
        option.MaxQuantity = dto.MaxQuantity;
        option.IsPublic = dto.IsPublic;
        option.IsActive = dto.IsActive;
        option.SortOrder = dto.SortOrder;
        option.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        var count = await db.AccountSubscriptionOptions.CountAsync(o => o.OptionId == id);
        return Ok(MapOptionDto(option, count));
    }

    [HttpDelete("options/{id:guid}")]
    public async Task<IActionResult> DeactivateOption(Guid id)
    {
        var option = await db.SubscriptionOptions.FindAsync(id);
        if (option is null) return NotFound();

        option.IsActive = false;
        option.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return NoContent();
    }

    [HttpGet("option-capabilities")]
    public IActionResult GetOptionCapabilities() =>
        Ok(new { capabilities = OptionCapabilityCatalog.Known.Select(c => new { c.Key, c.Kind, c.Name }).ToList() });

    // Contract §48: option codes are machine identifiers, not free text.
    private static readonly System.Text.RegularExpressions.Regex CodePattern =
        new("^[a-z0-9.\\-]{2,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // N — SubscriptionOption.PricePerMonth is `numeric(10,2)` (AppDbContext), 8 integer digits + 2
    // decimal, so anything at or above 10^8 overflows the column and Npgsql throws a raw
    // PostgresException ("numeric field overflow") on SaveChangesAsync — an unhandled 500, not a 400
    // (cycle-07 backend report, item 3, confirmed against the actual column precision). Validated here,
    // before the row ever reaches the DbContext, same as every other business rule in this method.
    public const decimal MaxOptionPricePerMonth = 99_999_999.99m;
    // No column-precision reason for this one (MaxQuantity is a plain `int`) — just a sane upper bound
    // so a denormalized value here can't later blow up a `decimal * int` multiplication elsewhere
    // (BillingCalculator.MonthlyPriceFor multiplies a subscribed quantity by the option's price).
    public const int MaxOptionMaxQuantity = 1_000_000;

    internal static IActionResult? ValidateOptionInput(AdminOptionInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || !CodePattern.IsMatch(dto.Code))
            return new BadRequestObjectResult("Код опции обязателен и должен соответствовать формату ^[a-z0-9.-]{2,64}$.");
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
            return new BadRequestObjectResult("Название обязательно (до 100 символов).");
        if (dto.PricePerMonth is < 0)
            return new BadRequestObjectResult("Цена не может быть отрицательной.");
        if (dto.PricePerMonth > MaxOptionPricePerMonth)
            return new BadRequestObjectResult($"Цена не может превышать {MaxOptionPricePerMonth}.");
        if (dto.MaxQuantity is not null && dto.MaxQuantity < 1)
            return new BadRequestObjectResult("Максимальное количество должно быть не меньше 1.");
        if (dto.MaxQuantity > MaxOptionMaxQuantity)
            return new BadRequestObjectResult($"Максимальное количество не может превышать {MaxOptionMaxQuantity}.");

        if (TryParseKind(dto.Kind) is not { } kind)
            return new BadRequestObjectResult("kind должен быть Toggle или Quantity.");
        if (kind == OptionKind.Quantity && string.IsNullOrWhiteSpace(dto.UnitName))
            return new BadRequestObjectResult("Для опции-количества обязательна единица измерения.");
        if (kind == OptionKind.Toggle && !string.IsNullOrWhiteSpace(dto.UnitName))
            return new BadRequestObjectResult("Для опции-переключателя единица измерения не задаётся.");

        return null;
    }

    private static OptionKind? TryParseKind(string? kind) => kind switch
    {
        "Toggle" => OptionKind.Toggle,
        "Quantity" => OptionKind.Quantity,
        _ => null,
    };

    private static OptionKind ParseKind(string kind) =>
        TryParseKind(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "kind must be Toggle or Quantity");

    private async Task<Dictionary<Guid, int>> GetOptionSubscriberCountsAsync(IEnumerable<Guid> optionIds)
    {
        var ids = optionIds.ToList();
        return await db.AccountSubscriptionOptions
            .Where(o => ids.Contains(o.OptionId) && (o.EndsAtUtc == null || o.EndsAtUtc > DateTime.UtcNow))
            .GroupBy(o => o.OptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OptionId, x => x.Count);
    }

    private static object MapOptionDto(SubscriptionOption o, int subscribedAccounts) => new
    {
        id = o.Id,
        code = o.Code,
        name = o.Name,
        description = o.Description,
        kind = o.Kind.ToString(),
        capabilityKey = o.CapabilityKey,
        capabilityKnown = OptionCapabilityCatalog.IsKnown(o.CapabilityKey),
        pricePerMonth = o.PricePerMonth,
        currency = "RUB",
        unitName = o.UnitName,
        maxQuantity = o.MaxQuantity,
        isPublic = o.IsPublic,
        isActive = o.IsActive,
        sortOrder = o.SortOrder,
        subscribedAccounts,
    };}

public record AdminOptionInput(
    string Code, string Name, string? Description, string Kind, string? CapabilityKey,
    decimal? PricePerMonth, string? UnitName, int? MaxQuantity, bool IsPublic = false, bool IsActive = true, int SortOrder = 0);
