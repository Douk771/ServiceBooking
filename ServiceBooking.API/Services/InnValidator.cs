namespace ServiceBooking.API.Services;

/// <summary>
/// Formal ИНН validation — length and checksum, exactly as the ФНС algorithm publishes it. No call to
/// ЕГРЮЛ/ЕГРИП (API_CONTRACT_CYCLE5.md §50.1, ПЛ5 — "контрольная сумма 10/12 знаков — это арифметика, а
/// не интеграция"). Pure, no DB, no HTTP — unit-tested directly.
/// </summary>
public static class InnValidator
{
    // ФНС's own published coefficients. Ten digits (organizations) have one check digit at index 9,
    // derived from the nine digits before it; twelve digits (individuals/sole proprietors) have TWO
    // check digits, at indices 10 and 11, each derived from everything before it.
    private static readonly int[] Coefficients10 = [2, 4, 10, 3, 5, 9, 4, 6, 8];
    private static readonly int[] Coefficients11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8];
    private static readonly int[] Coefficients12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8];

    public static bool IsValid(string? inn)
    {
        if (string.IsNullOrEmpty(inn)) return false;
        if (inn.Length != 10 && inn.Length != 12) return false;
        if (!inn.All(char.IsAsciiDigit)) return false;

        var digits = inn.Select(c => c - '0').ToArray();

        return inn.Length == 10
            ? CheckDigit(digits, Coefficients10) == digits[9]
            : CheckDigit(digits, Coefficients11) == digits[10] && CheckDigit(digits, Coefficients12) == digits[11];
    }

    private static int CheckDigit(int[] digits, int[] coefficients)
    {
        var sum = 0;
        for (var i = 0; i < coefficients.Length; i++)
            sum += digits[i] * coefficients[i];
        return sum % 11 % 10;
    }
}
