using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>Either the error the action returns as is, or the response (<see cref="Created"/>: 201, otherwise 200 for an idempotent repeat).</summary>
public sealed record OrderCreationResult(ActionResult? Error, CreateOrderResponse? Response = null, bool Created = false);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §395 (cycle 24: §451.2, API_CONTRACT_CYCLE24.md §478.1) — the storefront quote and the creation of an order. The
/// order of checks of <see cref="CreateAsync"/> is the contract's and each step has a reason: cheap model checks first; the shop; IDEMPOTENCY
/// (a repeat of an already created order must answer 200 even while the shop is paused, and must not hit the one-shot captcha or the
/// throttle); the acceptance rule; the cart size; the PICKUP TIME re-check (before the captcha, so a one-shot token is not burnt on a refusal
/// about time); the customer rules; the per-phone throttle; then ONE transaction (stock lock, products on the pickup date, quantities,
/// availability, stock, price, the MONTH counter, the number of the pickup day, snapshots, journal + revision + notifications). An order is
/// never created with other data than the customer confirmed: every problem is reported at once and nothing is written.
/// </summary>
public class OrderCreationService(
    AppDbContext db, CaptchaService captcha, SubjectScopeResolver subjectScope, PhoneVerificationAvailability phoneVerification,
    OrderPhoneThrottle throttle, StockLedger stockLedger, OrderNumberAllocator numberAllocator, OrderEventLog eventLog,
    OrderDtoMapper mapper, PublicSiteLinks links, LegalDocumentProvider legalProvider, IOptions<OrdersOptions> options,
    ShopGateLoader gates, DailyMenuService menus, OrderMonthlyCounter monthlyCounter, OrderLimitWarner limitWarner,
    CustomerOrderNotificationsBuilder notificationsBuilder, ShopChannelReader shopChannels)
{
    private const string IdempotencyIndex = "IX_Orders_CompanyId_IdempotencyKey";

    // ── Quote ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>POST /api/storefront/{slug}/quote — prices, totals and problems of a cart ON the chosen pickup date. Reserves nothing; 200 for any cart (§412, §477.4).</summary>
    public async Task<(ActionResult? Error, QuoteDto? Quote)> QuoteAsync(string slug, QuoteInput input, CancellationToken ct)
    {
        var items = input.Items ?? [];
        if (items.Count > options.Value.MaxLines) return (new BadRequestObjectResult("В корзине не больше 50 позиций"), null);
        if (items.Select(i => i.ProductId).Distinct().Count() != items.Count) return (new BadRequestObjectResult("Товар в корзине повторяется"), null);
        if (PickupSelectionError(input.Pickup) is { } pickupError) return (new BadRequestObjectResult(pickupError), null);

        var shop = await FindShopAsync(slug, ct);
        if (shop is null) return (new NotFoundResult(), null);
        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var gate = context.Gate;

        var selection = ToSelection(input.Pickup);
        var pickup = gate.Schedule.Validate(selection, now, forStaff: false);
        // A refused time still gets its dates checked: the customer changes the date and expects the cart lines to be judged on THAT date.
        var pickupDate = pickup.Ok ? pickup.PickupDate : selection.Kind == PickupKind.Slot && selection.Date is { } chosen ? chosen : gate.Schedule.CurrentWorkingDay(now);
        var menu = await menus.LookupAsync(shop.Id, pickupDate, ct);

        var evaluation = await EvaluateAsync(shop, context.Settings, items.Select(i => (i.ProductId, i.Quantity)).ToList(), null, pickupDate, menu, ct);
        var lines = new List<QuoteLineDto>();
        decimal total = 0;
        var approximate = false;
        foreach (var line in evaluation)
        {
            var p = line.Product;
            if (p is null || p.DeletedAtUtc is not null)
            {
                lines.Add(new QuoteLineDto(line.ProductId, OrderTexts.ProductUnavailable, ProductUnit.Piece, 0m, null, line.Quantity, 0m, false, line.Problem));
                continue;
            }
            // A line with a problem still shows what it would cost (greyed in the cart) but is left out of the total.
            var lineTotal = line.Quantity > 0 ? OrderMoney.LineTotal(p.Unit, p.Price, line.Quantity) : 0m;
            if (line.Problem is null)
            {
                total += lineTotal;
                approximate |= OrderMoney.IsApproximate(p.Unit);
            }
            lines.Add(new QuoteLineDto(p.Id, p.Name, p.Unit, p.Price, p.PortionText, line.Quantity, lineTotal, OrderMoney.IsApproximate(p.Unit), line.Problem));
        }
        var pickupProblem = pickup.Ok ? null : new PickupProblemDto(OrderRefusalCode.PickupTimeUnavailable, pickup.ProblemText!);
        return (null, new QuoteDto(
            lines, OrderMoney.Sum([total]), approximate, lines.Any(l => l.Problem is not null) || pickupProblem is not null,
            gate.Accepting, gate.ReasonText, pickupDate, pickupProblem));
    }

    // ── Create ───────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<OrderCreationResult> CreateAsync(
        string slug, CreateOrderInput dto, ClaimsPrincipal user, string? remoteIp, CancellationToken ct)
    {
        // 1. The model. Text fields are nullable in the DTO on purpose: the contract's own Russian sentences answer these.
        var customerName = (dto.CustomerName ?? string.Empty).Trim();
        if (customerName.Length is < 1 or > 100) return Bad("Укажите имя");
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment is { Length: > 500 }) return Bad("Комментарий — не длиннее 500 символов");
        var lines = dto.Items ?? [];
        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count) return Bad("Товар в корзине повторяется");
        if (dto.IdempotencyKey == Guid.Empty) return Bad("Не указан ключ заказа");
        if (PickupSelectionError(dto.Pickup) is { } pickupError) return Bad(pickupError);

        // 2. The shop: none / a salon → 404.
        var shop = await FindShopAsync(slug, ct);
        if (shop is null) return new OrderCreationResult(new NotFoundResult());

        // 3. Idempotency — a repeat with the same key returns the order that already exists (200), BEFORE the acceptance rule (a repeat of
        //    an order created a second ago must not turn into a refusal because the shop paused since), the captcha and the limits.
        var existing = await LoadOrderAsync(o => o.CompanyId == shop.Id && o.IdempotencyKey == dto.IdempotencyKey, ct);
        if (existing is not null) return new OrderCreationResult(null, await BuildResponseAsync(existing, shop, null, ct), Created: false);

        // 4. The acceptance rule (§450).
        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var settings = context.Settings;
        var gate = context.Gate;
        if (!gate.Accepting)
            return Refuse(OrderRefusalCode.ShopNotAcceptingOrders, gate.ReasonText ?? OrderTexts.ShopNotAvailable, notAcceptingCode: gate.Code);

        // 5. Cart size.
        if (lines.Count == 0) return Refuse(OrderRefusalCode.EmptyCart, OrderTexts.EmptyCart);
        if (lines.Count > options.Value.MaxLines) return Refuse(OrderRefusalCode.TooManyLines, OrderTexts.TooManyLines);

        // 6. The pickup time, re-checked NOW against the schedule (§451.2). An order is never created with another time than the one chosen.
        var pickup = gate.Schedule.Validate(ToSelection(dto.Pickup), now, forStaff: false);
        if (!pickup.Ok) return Refuse(OrderRefusalCode.PickupTimeUnavailable, pickup.ProblemText!);

        // 7. The customer.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = userId is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var scope = account is null ? (SubjectScope?)null : await subjectScope.ForAccountAsync(account, ct);
        // GuestMatchPhone is non-null exactly when a VerifiedPhones row exists for THIS account and its current number
        // (the cycle-16 source of truth — never the PhoneNumberConfirmed mirror).
        var accountPhoneVerified = scope?.GuestMatchPhone is not null;

        string canonicalPhone;
        OrderActorKind customerKind;
        if (settings.CustomerMode == ShopCustomerMode.VerifiedPhoneOnly)
        {
            if (account is null) return Refuse(OrderRefusalCode.LoginRequired, OrderTexts.LoginRequired);
            if (!accountPhoneVerified)
                return phoneVerification.IsAvailable
                    ? Refuse(OrderRefusalCode.PhoneVerificationRequired, OrderTexts.PhoneVerificationRequired)
                    : Refuse(OrderRefusalCode.PhoneVerificationUnavailable, OrderTexts.PhoneVerificationUnavailable);
            canonicalPhone = account.PhoneNumber!;
            customerKind = OrderActorKind.Customer;
        }
        else if (account is not null)
        {
            // A signed-in customer orders on THEIR number — a phone typed into the form is ignored, or the strictness of
            // "the account's number" would be dodged by typing someone else's (§405 deviation 3).
            if (account.PhoneNumber is not null) canonicalPhone = account.PhoneNumber;
            else if (!TryNormalizePhone(dto.CustomerPhone, out canonicalPhone, out var phoneError)) return Bad(phoneError!);
            customerKind = OrderActorKind.Customer;
        }
        else
        {
            // A guest: bot protection like a guest booking (SmartCaptcha; fails closed in Production), then the phone.
            if (captcha.IsEnforced)
            {
                if (string.IsNullOrEmpty(dto.CaptchaToken)) return Bad("Подтвердите, что вы не робот");
                if (!await captcha.ValidateAsync(dto.CaptchaToken, remoteIp)) return Bad("Проверка капчи не пройдена");
            }
            if (!TryNormalizePhone(dto.CustomerPhone, out canonicalPhone, out var phoneError)) return Bad(phoneError!);
            customerKind = OrderActorKind.Guest;
        }

        // 8. The per-phone throttle (the per-IP one is the "order-create" rate-limit policy).
        if (await throttle.IsLimitedAsync(shop.Id, canonicalPhone, now, ct))
            return new OrderCreationResult(new ObjectResult("Слишком много заказов на этот номер — дождитесь выдачи текущих или позвоните в магазин") { StatusCode = 429 });

        // [legal L9] The messenger choice counts only if the shop really offers it (a switched-on flag AND a funded number); otherwise it is silently off.
        var notifyByMessenger = dto.NotifyByMessenger && settings.CustomerMessengerEnabled && await shopChannels.IsMessengerAvailableAsync(shop.Id, ct);

        // 9–10. One transaction. With stock tracking the shop's stock lock is held for the whole of it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (settings.TrackStock) await stockLedger.LockAsync(shop.Id);

        var menu = await menus.LookupAsync(shop.Id, pickup.PickupDate, ct);
        var evaluated = await EvaluateAsync(shop, settings, lines.Select(l => (l.ProductId, l.Quantity)).ToList(), lines, pickup.PickupDate, menu, ct);
        var problems = evaluated.Where(e => e.Problem is not null).Select(e => e.Problem!).ToList();
        if (problems.Count > 0)
        {
            var onlyPrices = problems.All(p => p.Reason == OrderProblemReason.PriceChanged);
            return onlyPrices
                ? Refuse(OrderRefusalCode.PriceChanged, OrderTexts.PriceChanged, problems)
                : Refuse(OrderRefusalCode.ItemsUnavailable, OrderTexts.ItemsUnavailable, problems);
        }

        // The month counter (§459.4): one upsert whose row lock serializes the orders of the ACCOUNT; over the limit → the whole transaction rolls back.
        var businessDate = ShopClock.BusinessDate(shop.TimeZoneId, now);
        var month = ShopGateLoader.MonthStart(businessDate);
        MonthlyUsage? usage = null;
        if (shop.BillingAccountId is { } billingAccountId)
        {
            usage = await monthlyCounter.IncrementAsync(billingAccountId, month, ct);
            if (context.Plan.MaxOrdersPerMonth is { } monthlyLimit && usage.Count > monthlyLimit)
                return Refuse(OrderRefusalCode.ShopNotAcceptingOrders, ShopOrderingGate.TemporarilyNotAccepting, notAcceptingCode: ShopNotAcceptingCode.MonthlyLimitReached);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CompanyId = shop.Id,
            Number = await numberAllocator.NextAsync(shop.Id, pickup.PickupDate, ct),
            BusinessDate = businessDate,
            PickupKind = pickup.EndUtc is null ? PickupKind.Asap : PickupKind.Slot,
            PickupDate = pickup.PickupDate,
            PickupStartUtc = pickup.StartUtc,
            PickupEndUtc = pickup.EndUtc,
            NotifyByMessenger = notifyByMessenger,
            MessengerConsentVersion = notifyByMessenger ? legalProvider.Current?.GetText(LegalTextKey.OrderMessengerConsent)?.Version : null,
            MessengerConsentAtUtc = notifyByMessenger ? now : null,
            PublicToken = PublicOrderToken.Generate(),
            Status = settings.AcceptanceMode == OrderAcceptanceMode.Auto ? OrderStatus.Accepted : OrderStatus.New,
            Version = 1,
            CustomerKind = customerKind,
            CustomerUserId = customerKind == OrderActorKind.Customer ? account!.Id : null,
            CustomerName = customerName,
            CustomerPhone = canonicalPhone,
            CustomerPhoneVerified = customerKind == OrderActorKind.Customer && accountPhoneVerified,
            Comment = comment,
            AcceptanceModeSnapshot = settings.AcceptanceMode,
            AllowCustomerCancelSnapshot = settings.AllowCustomerCancel,
            CustomerModeSnapshot = settings.CustomerMode,
            IdempotencyKey = dto.IdempotencyKey,
            CheckoutNoticeVersion = legalProvider.Current?.GetText(LegalTextKey.OrderCheckoutNotice)?.Version,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        order.AcceptedAtUtc = order.Status == OrderStatus.Accepted ? now : null;

        // A guest's consent snapshot exactly like a guest booking (§398.3): the versions in force right now, from the server.
        if (customerKind == OrderActorKind.Guest)
        {
            var snapshot = legalProvider.Current;
            var privacy = snapshot?.Get(LegalDocumentType.Privacy);
            var terms = snapshot?.Get(LegalDocumentType.TermsClient);
            if (privacy is not null && terms is not null)
            {
                order.ConsentPrivacyVersion = privacy.Version;
                order.ConsentTermsVersion = terms.Version;
                order.ConsentAcceptedAtUtc = now;
            }
        }

        var position = 0;
        foreach (var e in evaluated)
        {
            var p = e.Product!;
            var lineTotal = OrderMoney.LineTotal(p.Unit, p.Price, e.Quantity);
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(), OrderId = order.Id, Position = position++, ProductId = p.Id, NameSnapshot = p.Name,
                Unit = p.Unit, UnitPrice = p.Price, PortionTextSnapshot = p.PortionText, WeightStepGrams = p.WeightStepGrams,
                QuantityOrdered = e.Quantity, LineTotalEstimated = lineTotal,
                // Reserves stock only if the shop tracks it AND the product has a stock figure right now (US-23-17).
                ReservesStock = settings.TrackStock && p.StockOnHand is not null,
            });
        }
        order.EstimatedTotal = OrderMoney.Sum(order.Items.Select(i => i.LineTotalEstimated));
        order.HasWeightItems = order.Items.Any(i => i.Unit == ProductUnit.Weight);

        db.Orders.Add(order);
        await eventLog.AppendAsync(order, OrderEventKind.Created,
            new OrderActor(customerKind, order.CustomerUserId, customerName), null, order.Status);
        if (usage is not null && shop.BillingAccountId is { } accountId)
            await limitWarner.AfterIncrementAsync(shop, accountId, month, usage, context.Plan, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
                                            pg.ConstraintName == IdempotencyIndex)
        {
            // A concurrent request with the same key won the race: return ITS order, not a second one.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await LoadOrderAsync(o => o.CompanyId == shop.Id && o.IdempotencyKey == dto.IdempotencyKey, ct);
            if (winner is null) throw;
            return new OrderCreationResult(null, await BuildResponseAsync(winner, shop, null, ct), Created: false);
        }

        return new OrderCreationResult(null, await BuildResponseAsync(order, shop, settings, ct), Created: true);
    }

    // ── Pickup selection ─────────────────────────────────────────────────────────────────────────────────────────

    public const string PickupSelectionIncomplete = "Укажите дату и время получения";

    /// <summary>A Slot without a date or a start time is a malformed request (400); everything else about the time is the schedule's verdict (409).</summary>
    private static string? PickupSelectionError(PickupSelectionInput? pickup) =>
        pickup is { Kind: PickupKind.Slot } && (pickup.Date is null || pickup.SlotStartUtc is null) ? PickupSelectionIncomplete : null;

    /// <summary>No <c>pickup</c> = "as soon as possible": the cycle-23 frontend in the roll-out window keeps working.</summary>
    private static PickupSelection ToSelection(PickupSelectionInput? pickup) =>
        pickup is null ? new PickupSelection(PickupKind.Asap, null, null) : new PickupSelection(pickup.Kind, pickup.Date, pickup.SlotStartUtc);

    // ── Evaluation (shared by quote and create) ──────────────────────────────────────────────────────────────────

    private sealed record EvaluatedLine(Guid ProductId, int Quantity, Product? Product, OrderProblemDto? Problem);

    /// <summary>
    /// One verdict per cart line: not found / not published / hidden category / not sold that DATE / sold out / quantity rule / stock — and, when
    /// <paramref name="orderLines"/> are given (creation), whether the price the customer saw is still the current one.
    /// </summary>
    private async Task<List<EvaluatedLine>> EvaluateAsync(
        Company shop, ShopSettings settings, IReadOnlyList<(Guid ProductId, int Quantity)> cart,
        IReadOnlyList<OrderLineInput>? orderLines, DateOnly pickupDate, DailyMenuLookup menu, CancellationToken ct)
    {
        var ids = cart.Select(c => c.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => p.CompanyId == shop.Id && ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == shop.Id).ToDictionaryAsync(c => c.Id, ct);
        var reserved = settings.TrackStock ? await stockLedger.GetReservedAsync(shop.Id, ids, ct) : new Dictionary<Guid, int>();
        var expectedPrices = orderLines?.ToDictionary(l => l.ProductId, l => l.ExpectedUnitPrice);

        var result = new List<EvaluatedLine>();
        foreach (var (productId, quantity) in cart)
        {
            products.TryGetValue(productId, out var product);
            var problem = ProblemFor(productId, product, quantity, settings, categories, reserved.GetValueOrDefault(productId), pickupDate, menu);
            if (problem is null && expectedPrices is not null && product is not null && expectedPrices[productId] != product.Price)
                problem = new OrderProblemDto(product.Id, product.Name, OrderProblemReason.PriceChanged,
                    OrderTexts.PriceWas(expectedPrices[productId], product.Price), CurrentUnitPrice: product.Price);
            result.Add(new EvaluatedLine(productId, quantity, product, problem));
        }
        return result;
    }

    private static OrderProblemDto? ProblemFor(
        Guid productId, Product? product, int quantity, ShopSettings settings,
        IReadOnlyDictionary<Guid, ProductCategory> categories, int reserved, DateOnly pickupDate, DailyMenuLookup menu)
    {
        if (product is null || product.DeletedAtUtc is not null)
            return new OrderProblemDto(productId, OrderTexts.ProductUnavailable, OrderProblemReason.NotFound, OrderTexts.ProductUnavailable);

        var category = product.CategoryId is { } cid ? categories.GetValueOrDefault(cid) : null;
        var free = StockLedger.Free(product.StockOnHand, reserved);
        var verdict = CatalogAvailability.Evaluate(product, category, settings.TrackStock, free, shopAccepting: true, pickupDate, menu);
        OrderProblemDto Problem(OrderProblemReason reason, int? available = null) => new(
            product.Id, product.Name, reason,
            OrderTexts.ProblemMessage(reason, product.Unit, available, OrderQuantityRules.MinQuantity(product.Unit, product.WeightStepGrams, product.MinQuantityGrams)),
            AvailableQuantity: available);

        switch (verdict)
        {
            case ProductAvailability.Unpublished: return Problem(OrderProblemReason.Unpublished);
            case ProductAvailability.CategoryHidden: return Problem(OrderProblemReason.CategoryHidden);
            case ProductAvailability.NotOnThisDate: return Problem(OrderProblemReason.NotAvailableOnDate);
            case ProductAvailability.SoldOut: return Problem(OrderProblemReason.SoldOut);
            case ProductAvailability.InsufficientStock: return StockProblem(product, free!.Value, Problem);
        }

        switch (OrderQuantityRules.Check(product.Unit, quantity, product.WeightStepGrams, product.MinQuantityGrams))
        {
            case QuantityCheck.InvalidQuantity: return Problem(OrderProblemReason.InvalidQuantity);
            case QuantityCheck.BelowMinimum: return Problem(OrderProblemReason.BelowMinimum);
        }

        if (settings.TrackStock && free is not null)
        {
            var available = OrderQuantityRules.AvailableQuantity(product.Unit, free.Value, product.WeightStepGrams, product.MinQuantityGrams);
            if (quantity > available) return StockProblem(product, free.Value, Problem);
        }
        return null;
    }

    private static OrderProblemDto StockProblem(Product product, int free, Func<OrderProblemReason, int?, OrderProblemDto> problem) =>
        problem(OrderProblemReason.InsufficientStock,
            OrderQuantityRules.AvailableQuantity(product.Unit, free, product.WeightStepGrams, product.MinQuantityGrams));

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<Company?> FindShopAsync(string slug, CancellationToken ct)
    {
        var normalized = SlugPolicy.Normalize(slug);
        return db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == normalized && c.Kind == CompanyKind.Orders, ct);
    }

    private Task<Order?> LoadOrderAsync(System.Linq.Expressions.Expression<Func<Order, bool>> predicate, CancellationToken ct) =>
        db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).AsSplitQuery().FirstOrDefaultAsync(predicate, ct);

    private async Task<CreateOrderResponse> BuildResponseAsync(Order order, Company shop, ShopSettings? settings, CancellationToken ct)
    {
        settings ??= await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var pickupContext = await gates.PickupContextAsync(shop, settings, DateTime.UtcNow, ct);
        return new CreateOrderResponse(
            mapper.ToPublic(order, shop, cityName, notificationsBuilder.Build(order, settings), pickupContext), links.OrderPageUrl(order.PublicToken));
    }

    private static bool TryNormalizePhone(string? raw, out string canonical, out string? error)
    {
        canonical = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Укажите телефон";
            return false;
        }
        if (!PhoneNormalizer.TryNormalizeRussian(raw, out canonical))
        {
            error = "Введите номер телефона в формате +7 (900) 000-00-00";
            return false;
        }
        return true;
    }

    private static OrderCreationResult Bad(string message) => new(new BadRequestObjectResult(message));

    private static OrderCreationResult Refuse(
        OrderRefusalCode code, string message, List<OrderProblemDto>? problems = null, ShopNotAcceptingCode? notAcceptingCode = null) =>
        new(new ConflictObjectResult(new OrderRefusalDto(code, message, problems, notAcceptingCode)));
}
