namespace ServiceBooking.API.Services.Billing;

/// <summary>Server-assembled Russian copy for billing/funding surfaces (ARCHITECTURE_CYCLE5.md §41
/// п. 8, as <c>NotificationTexts</c> is for cycle 4) — the frontend never composes these sentences
/// itself. The word "биллинг-аккаунт" never appears here (§41 п. 8, Р8): every text below is shown to
/// the OWNER, not an admin.</summary>
public static class BillingTexts
{
    /// <summary>402 body for <c>CompaniesController.AddMember</c> (§46.4) — the seat limit is now
    /// summed across every company on the account, so the text says so explicitly rather than leaving
    /// the owner to wonder why a company with 2 staff hit an 8-seat cap.</summary>
    public static string SeatLimitReached(int used, int limit) =>
        $"Занято {used} из {limit} мест. Лимит общий на все ваши точки.";

    public static string CompanyLimitReached(int used, int limit) =>
        $"Открыто {used} из {limit} точек, доступных на вашем тарифе.";

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
