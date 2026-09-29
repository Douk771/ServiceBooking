using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393, §410 — the catalog of a shop: categories, products, their order, the photo, "sold out" and the
/// stock figure. Reading is for the owner and staff; everything that changes the catalog is the owner's (staff may only mark a
/// product sold out and correct the stock — §392.2). Order of checks: token → the shop exists (404) → the role (403) → the
/// entity of the route (404) → validation (400) → conflicts (409, JSON codes of <c>CatalogConflictDto</c>).
/// Lists are returned whole, without PagedResult — bounded collections (≤ 1000 products / 100 categories), §393.4.
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}")]
[Authorize]
public class ShopCatalogController(
    AppDbContext db, ShopAccessResolver access, CatalogMapper mapper, StockLedger stockLedger,
    ImageUploadService imageUploadService, FileStorage storage, IOptions<OrdersOptions> ordersOptions) : ControllerBase
{
    // ── Categories ───────────────────────────────────────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == shopId)
            .OrderBy(c => c.Position).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var counts = await ProductCountsAsync(shopId, ct);
        return Ok(categories.Select(c => ToDto(c, counts)).ToList());
    }

    [HttpPost("categories")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CategoryDto>> CreateCategory(Guid shopId, CategoryInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest(ShopTexts.CategoryNameRequired);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        var existing = await db.ProductCategories.Where(c => c.CompanyId == shopId).Select(c => c.Position).ToListAsync(ct);
        var limit = ordersOptions.Value.MaxCategoriesPerShop;
        if (existing.Count >= limit)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.CategoryLimitReached, ShopTexts.CategoryLimitReached(limit)));

        var category = new ProductCategory
        {
            Id = Guid.NewGuid(), CompanyId = shopId, Name = name, IsHidden = input.IsHidden,
            Position = CatalogOrdering.NextPosition(existing)
        };
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, ToDto(category, new Dictionary<Guid, int>()));
    }

    [HttpPut("categories/{categoryId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(Guid shopId, Guid categoryId, CategoryInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.CompanyId == shopId, ct);
        if (category is null) return NotFound();
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest(ShopTexts.CategoryNameRequired);

        category.Name = name;
        category.IsHidden = input.IsHidden;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(category, await ProductCountsAsync(shopId, ct)));
    }

    [HttpPut("category-order")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<List<CategoryDto>>> ReorderCategories(Guid shopId, UuidList input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        var current = await db.ProductCategories.Where(c => c.CompanyId == shopId).ToListAsync(ct);
        try
        {
            CatalogOrdering.ApplyCategoryOrder(current, input.Ids ?? []);
        }
        catch (InvalidCatalogReorderException ex)
        {
            return BadRequest(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var counts = await ProductCountsAsync(shopId, ct);
        return Ok(current.OrderBy(c => c.Position).Select(c => ToDto(c, counts)).ToList());
    }

    [HttpDelete("categories/{categoryId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeleteCategory(Guid shopId, Guid categoryId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.CompanyId == shopId, ct);
        if (category is null) return NotFound();
        if (await db.Products.AnyAsync(p => p.CategoryId == categoryId && p.DeletedAtUtc == null, ct))
            return Conflict(new CatalogConflictDto(CatalogConflictCode.CategoryNotEmpty, ShopTexts.CategoryNotEmpty));

        // Soft-deleted products still hold the foreign key (orders reference them) but are invisible everywhere — release it.
        await db.Products.Where(p => p.CategoryId == categoryId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, (Guid?)null), ct);
        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        var remaining = await db.ProductCategories.Where(c => c.CompanyId == shopId).ToListAsync(ct);
        CatalogOrdering.Compact(remaining);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    // ── Products ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The whole (bounded) product list for the cabinet; <c>categoryId=none</c> — products without a category.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<List<ProductDto>>> GetProducts(
        Guid shopId, [FromQuery] string? search, [FromQuery] string? categoryId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var query = db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            if (string.Equals(categoryId, "none", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p => p.CategoryId == null);
            else if (Guid.TryParse(categoryId, out var parsedCategory))
                query = query.Where(p => p.CategoryId == parsedCategory);
            else
                return BadRequest("Категория указана неверно");
        }
        var trimmedSearch = ServiceBooking.API.DTOs.Common.Pagination.SanitizeSearch(search);
        if (!string.IsNullOrWhiteSpace(trimmedSearch))
        {
            var pattern = $"%{CompaniesController.EscapeLikeWildcards(trimmedSearch.Length > 100 ? trimmedSearch[..100] : trimmedSearch)}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern));
        }

        var products = await query.ToListAsync(ct);
        return Ok(await mapper.BuildManyAsync(result.Shop!, products, ct));
    }

    [HttpPost("products")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ProductDto>> CreateProduct(Guid shopId, ProductInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        if (!ProductInputRules.TryNormalize(input.Name, input.Description, input.Price, input.Unit, input.PortionText,
                input.WeightStepGrams, input.MinQuantityGrams, input.FoodInfo?.CompositionAndAllergens, out var normalized, out var error))
            return BadRequest(error);
        if (input.CategoryId is { } categoryId && !await db.ProductCategories.AnyAsync(c => c.Id == categoryId && c.CompanyId == shopId, ct))
            return BadRequest(ShopTexts.CategoryNotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        var limit = ordersOptions.Value.MaxProductsPerShop;
        if (await db.Products.CountAsync(p => p.CompanyId == shopId && p.DeletedAtUtc == null, ct) >= limit)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.ProductLimitReached, ShopTexts.ProductLimitReached(limit)));

        var positions = await db.Products.Where(p => p.CompanyId == shopId && p.CategoryId == input.CategoryId && p.DeletedAtUtc == null)
            .Select(p => p.Position).ToListAsync(ct);
        var product = new Product
        {
            Id = Guid.NewGuid(), CompanyId = shopId, CategoryId = input.CategoryId, Unit = input.Unit,
            IsPublished = input.IsPublished, Position = CatalogOrdering.NextPosition(positions)
        };
        Apply(product, normalized!);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    [HttpPut("products/{productId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid shopId, Guid productId, ProductInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();

        // The unit decides the meaning of stock and of the quantities of orders already placed — never changed.
        if (input.Unit != product.Unit)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.UnitChangeNotAllowed, ShopTexts.UnitChangeNotAllowed));
        if (!ProductInputRules.TryNormalize(input.Name, input.Description, input.Price, product.Unit, input.PortionText,
                input.WeightStepGrams, input.MinQuantityGrams, input.FoodInfo?.CompositionAndAllergens, out var normalized, out var error))
            return BadRequest(error);
        if (input.CategoryId is { } categoryId && !await db.ProductCategories.AnyAsync(c => c.Id == categoryId && c.CompanyId == shopId, ct))
            return BadRequest(ShopTexts.CategoryNotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        if (input.CategoryId != product.CategoryId)
        {
            // Moved to another category: it goes last there.
            var positions = await db.Products.Where(p => p.CompanyId == shopId && p.CategoryId == input.CategoryId && p.DeletedAtUtc == null)
                .Select(p => p.Position).ToListAsync(ct);
            product.CategoryId = input.CategoryId;
            product.Position = CatalogOrdering.NextPosition(positions);
        }
        product.IsPublished = input.IsPublished;
        Apply(product, normalized!);
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    [HttpDelete("products/{productId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeleteProduct(Guid shopId, Guid productId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();

        // Soft delete: orders reference the row and cycle 3's reports need it. Existing orders keep their snapshots.
        product.DeletedAtUtc = DateTime.UtcNow;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("product-order")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<List<ProductDto>>> ReorderProducts(Guid shopId, ProductOrderInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"shop-catalog:{shopId}");
        var current = await db.Products.Where(p => p.CompanyId == shopId && p.CategoryId == input.CategoryId && p.DeletedAtUtc == null).ToListAsync(ct);
        try
        {
            CatalogOrdering.ApplyProductOrder(current, input.ProductIds ?? []);
        }
        catch (InvalidCatalogReorderException ex)
        {
            return BadRequest(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(await mapper.BuildManyAsync(result.Shop!, current, ct));
    }

    // ── Photo ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Uploads/replaces the photo: the existing pipeline (byte signature, EXIF stripped, re-encoded), two renditions, public class of storage.</summary>
    [HttpPost("products/{productId:guid}/image")]
    [RequiresOwnerTerms]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ProductDto>> UploadImage(Guid shopId, Guid productId, IFormFile? file, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();

        var validation = await imageUploadService.ReadAndProcessAsync(file, [ImageProfile.ProductImage, ImageProfile.ProductImageThumb]);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        // Order matters (the project's file convention): new files → commit the row → delete the old files.
        var (oldUrl, oldThumb) = (product.ImageUrl, product.ThumbnailUrl);
        var full = validation.Images![0];
        var thumb = validation.Images![1];
        var newUrl = await storage.SavePublicAsync(PublicArea.Products, full.Bytes, full.Extension);
        var newThumb = await storage.SavePublicAsync(PublicArea.Products, thumb.Bytes, thumb.Extension);
        product.ImageUrl = newUrl;
        product.ThumbnailUrl = newThumb;
        product.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.DeletePublic(newUrl);
            storage.DeletePublic(newThumb);
            throw;
        }
        storage.DeletePublic(oldUrl);
        storage.DeletePublic(oldThumb);
        return Ok(await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    [HttpDelete("products/{productId:guid}/image")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ProductDto>> DeleteImage(Guid shopId, Guid productId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditCatalog, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();

        var (oldUrl, oldThumb) = (product.ImageUrl, product.ThumbnailUrl);
        product.ImageUrl = null;
        product.ThumbnailUrl = null;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        storage.DeletePublic(oldUrl); // row first, file after (a leftover file is swept later; a dangling row is never allowed)
        storage.DeletePublic(oldThumb);
        return Ok(await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    // ── Sold out and stock (owner and staff) ─────────────────────────────────────────────────────────────────────

    [HttpPut("products/{productId:guid}/sold-out")]
    public async Task<ActionResult<ProductDto>> SetSoldOut(Guid shopId, Guid productId, SoldOutInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageStock, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();

        product.IsSoldOut = input.IsSoldOut;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    /// <summary>
    /// The stock figure of a product. Under the shop's stock lock, like everything that can lower the free stock (§394.3): it may be
    /// set below the current reserve (a shortage found on the shelf) — the product then shows "sold out" to customers and the cabinet warns.
    /// </summary>
    [HttpPut("products/{productId:guid}/stock")]
    public async Task<ActionResult<ProductDto>> SetStock(Guid shopId, Guid productId, StockInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageStock, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        if (input.OnHand is < 0 or > 100_000_000) return BadRequest(ShopTexts.StockInvalid);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await stockLedger.LockAsync(shopId);
        var product = await FindLiveProductAsync(shopId, productId, ct);
        if (product is null) return NotFound();
        product.StockOnHand = input.OnHand;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(await mapper.BuildOneAsync(result.Shop!, product, ct));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<Product?> FindLiveProductAsync(Guid shopId, Guid productId, CancellationToken ct) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.CompanyId == shopId && p.DeletedAtUtc == null, ct);

    private static void Apply(Product product, NormalizedProductInput n)
    {
        product.Name = n.Name;
        product.Description = n.Description;
        product.Price = n.Price;
        product.PortionText = n.PortionText;
        product.WeightStepGrams = n.WeightStepGrams;
        product.MinQuantityGrams = n.MinQuantityGrams;
        product.CompositionAndAllergens = n.CompositionAndAllergens;
    }

    private async Task<Dictionary<Guid, int>> ProductCountsAsync(Guid shopId, CancellationToken ct)
    {
        var rows = await db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null && p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Count);
    }

    private static CategoryDto ToDto(ProductCategory c, IReadOnlyDictionary<Guid, int> counts) =>
        new(c.Id, c.Name, c.Position, c.IsHidden, counts.GetValueOrDefault(c.Id));
}
