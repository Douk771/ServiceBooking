namespace ServiceBooking.API.Services.Billing;

/// <summary>Server-assembled Russian copy for billing/funding surfaces (ARCHITECTURE_CYCLE7.md §41
/// п. 8, as <c>NotificationTexts</c> is for cycle 4) — the frontend never composes these sentences
/// itself. The word "биллинг-аккаунт" never appears here (§41 п. 8, Р8): every text below is shown to
/// the OWNER, not an admin.</summary>
public static class BillingTexts
{
    /// <summary>402 body for <c>CompaniesController.AddMember</c> (ARCHITECTURE_CYCLE19.md §384.3/
    /// §411/§414) — the seat limit is summed across every company on the account. After cycle 19 there
    /// is no "purchased" component and no call to action to buy an option (limits come only from the
    /// tariff), so this text no longer breaks the number down — <paramref name="limit"/> already
    /// includes the grandfathered bonus, which stays unnamed to the owner (§54.4, unchanged).</summary>
    public static string SeatLimitReached(int used, string planName, int limit) =>
        $"Занято {used} из {limit} мест — столько включено в тариф «{planName}». Лимит общий на все " +
        "ваши точки. Чтобы добавить сотрудника, выберите тариф с большим лимитом в разделе «Ваша подписка».";

    public static string CompanyLimitReached(int used, int limit) =>
        $"Открыто {used} из {limit} точек, доступных на вашем тарифе.";

    /// <summary>§51.1's 409 body when the proposed new owner isn't linked to the receiving account —
    /// names both fixes plus the third way out (transfer without changing the owner), per acceptance
    /// criteria.</summary>
    public static string TransferRejectedUnlinkedOwner(string newOwnerName) =>
        $"{newOwnerName} не связан(а) с принимающим аккаунтом. Сделайте его(её) держателем этого " +
        "аккаунта, добавьте сотрудником в любую его компанию — или выполните перенос без смены " +
        "ответственного и смените ответственного отдельно.";

    /// <summary>ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.1/§437.2 (US-20-07, LG6) —
    /// a transfer WITHOUT a new owner, where the company's existing owner has no link to the receiving
    /// account. Names the fix (pick a new owner from the target account) — the reverse of
    /// <see cref="TransferRejectedUnlinkedOwner"/>'s "или перенос без смены и смените отдельно", since
    /// that escape hatch is exactly what this text is rejecting.</summary>
    public static string TransferRejectedCurrentOwnerUnlinked(string companyName) =>
        $"Ответственный за компанию {companyName} не связан с принимающим аккаунтом. Укажите нового " +
        "ответственного из этого аккаунта.";

    /// <summary>§51.2's 402 body — the transfer is rejected outright, nothing is written. After cycle 19
    /// (ARCHITECTURE_CYCLE19.md §411/§414) there is no option to buy — the fix is a bigger tariff.</summary>
    public static string TransferRejectedCompanyLimit(string planName, int used, int limit) =>
        $"На тарифе «{planName}» — {limit} {CompaniesWord(limit)}, занято {used}. Чтобы принять ещё " +
        "одну, назначьте принимающему аккаунту тариф с большим лимитом компаний.";

    /// <summary>ARCHITECTURE_CYCLE19.md §403/§414 — 400 on POST/PUT of an option whose capabilityKey
    /// (trimmed, lowercased) is "employees" or "companies": those capabilities can no longer be sold as
    /// an option because the limit formula (<see cref="AccountLimitFormula"/>) never reads purchases.</summary>
    public const string LimitCapabilityNotSellable =
        "Возможности «employees» и «companies» нельзя продавать опцией: лимиты сотрудников и компаний " +
        "задаются только полями тарифа «Макс. сотрудников» и «Макс. компаний».";

    /// <summary>ARCHITECTURE_CYCLE19.md §403/§414 — 409 on PUT of an option that is already a retired
    /// limit option (<see cref="RetiredLimitOptions"/>): its row stays in the catalog for the deploy
    /// report but can no longer be edited.</summary>
    public static string RetiredOptionNotEditable(string name) =>
        $"Опция «{name}» выведена из оборота: лимиты сотрудников и компаний задаются только тарифом. " +
        "Изменить её нельзя.";

    /// <summary>ARCHITECTURE_CYCLE19.md §406/§408/§414 — 400 when a retired limit option is submitted
    /// in a subscription assignment or an owner's request; used for both surfaces with the same
    /// wording.</summary>
    public static string RetiredOptionRejected(string name) =>
        $"Опция «{name}» больше не подключается: лимиты сотрудников и компаний задаются только тарифом.";

    /// <summary>ARCHITECTURE_CYCLE19.md §408/§414 — non-null only when a pending request (submitted
    /// before the cycle 19 rollout) still names retired limit options; <paramref name="names"/> is the
    /// distinct, order-preserving list of such options' <see cref="ServiceBooking.Core.Entities.SubscriptionOption.Name"/>.</summary>
    public static string RetiredOptionsInRequestNotice(IReadOnlyList<string> names) =>
        "В заявке есть опции, которые больше не подключаются: " +
        string.Join(", ", names.Select(n => $"«{n}»")) +
        ". Лимиты сотрудников и компаний задаются только тарифом, поэтому при одобрении заявки эти " +
        "опции применены не будут.";

    private static string CompaniesWord(int n)
    {
        var lastTwo = n % 100;
        if (lastTwo is >= 11 and <= 14) return "компаний";
        return (n % 10) switch
        {
            1 => "компания",
            2 or 3 or 4 => "компании",
            _ => "компаний",
        };
    }

    /// <summary>
    /// §47.1's four required texts. <paramref name="workingPhoneMasked"/> is the earliest-created
    /// funded channel's masked phone — required (not just nice-to-have) for <c>Unfunded</c>, since the
    /// acceptance criterion is "names the working number", not just "explains the count".
    /// </summary>
    public static string FundingText(ChannelFundingState state, int paidNumbers, int liveCount, string? workingPhoneMasked)
    {
        switch (state)
        {
            case ChannelFundingState.NotPaid:
                return "Рассылки не отправляются: опция «Рассылки в WhatsApp» не оплачена. " +
                       "Чтобы включить, подключите её в разделе «Ваша подписка».";

            case ChannelFundingState.Funded when liveCount <= paidNumbers:
                return "Номер оплачен и работает.";

            case ChannelFundingState.Funded:
                return $"Номер работает: оплачен {paidNumbers} {NumbersWord(paidNumbers)} из {liveCount} заведённых, " +
                       "и он подключён раньше остальных.";

            case ChannelFundingState.Unfunded:
                var workingNote = workingPhoneMasked is null ? "другой, подключённый раньше" : workingPhoneMasked;
                return $"Этот номер не отправляет сообщения: у вас оплачен {paidNumbers} {NumbersWord(paidNumbers)}, " +
                       $"а заведено {liveCount}. Работает тот, что подключён раньше ({workingNote}). " +
                       "Чтобы включить и этот, подключите ещё одну «Рассылку в WhatsApp» — или удалите лишний номер.";

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private static string NumbersWord(int n)
    {
        var lastTwo = n % 100;
        if (lastTwo is >= 11 and <= 14) return "номеров";
        return (n % 10) switch
        {
            1 => "номер",
            2 or 3 or 4 => "номера",
            _ => "номеров",
        };
    }
}
