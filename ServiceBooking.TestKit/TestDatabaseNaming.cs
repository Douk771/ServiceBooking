using System.Text.RegularExpressions;

namespace ServiceBooking.TestKit;

/// <summary>
/// Единственное место, решающее «эту базу можно удалить».
/// Никакой другой код в репозитории не имеет права вызывать DROP DATABASE / EnsureDeletedAsync —
/// см. приёмочный грep в ARCHITECTURE_CYCLE8.md §79 п.1.
/// </summary>
public static class TestDatabaseNaming
{
    private const int PostgresMaxIdentifierLength = 63;

    // \A...\z (not ^...$): in .NET, "$" matches immediately before a trailing '\n', so a name like
    // "sbtest_a3f19c7b_api\n" used to pass this check (review finding N1) even though it is not a
    // valid Postgres identifier and was never something CREATE/DROP DATABASE actually produced.
    private static readonly Regex DisposableNamePattern =
        new(@"\Asbtest_[0-9a-f]{8}_[a-z][a-z0-9]{0,11}\z", RegexOptions.Compiled);

    public static readonly string[] NeverDrop =
        ["postgres", "template0", "template1", "servicebooking", "servicebooking_test"];

    /// <summary>true, если имя формально является одноразовой тестовой базой цикла 8
    /// (sbtest_&lt;8 hex&gt;_&lt;slot&gt;) и не входит в защищённый список.</summary>
    public static bool IsDisposable(string? databaseName)
    {
        if (string.IsNullOrEmpty(databaseName))
            return false;

        if (databaseName.Length > PostgresMaxIdentifierLength)
            return false;

        if (NeverDrop.Contains(databaseName))
            return false;

        return DisposableNamePattern.IsMatch(databaseName);
    }

    /// <summary>Бросает <see cref="TestSafetyException"/>, если <paramref name="databaseName"/>
    /// не является одноразовой тестовой базой. Вызывать первой строкой перед любым DROP DATABASE.</summary>
    public static void EnsureDisposable(string? databaseName)
    {
        if (IsDisposable(databaseName))
            return;

        throw new TestSafetyException(
            $"[sb-test] Отказ: прогон попытался удалить базу \"{databaseName}\", не являющуюся одноразовой тестовой.\n" +
            "  Ожидался формат: sbtest_<8 hex — ключ прогона>_<слот>, и имя вне списка защищённых\n" +
            "    (postgres, template0, template1, servicebooking, servicebooking_test).\n" +
            "  Что сделать: проверьте SERVICEBOOKING_TEST_CONNECTION — с цикла 8 эта переменная задаёт СЕРВЕР,\n" +
            "    а не базу; имя базы прогон выбирает сам. Ничего не удалено.");
    }

    /// <summary>Бросает <see cref="TestSafetyException"/>, если сегмент ключа прогона в имени базы
    /// не совпадает с <see cref="TestRunKey.Current"/>. Защищает от удаления базы чужого прогона.</summary>
    public static void EnsureOwnedByThisRun(string? databaseName)
    {
        EnsureDisposable(databaseName);

        var ownerKey = databaseName!.Split('_', 3)[1];
        if (!string.Equals(ownerKey, TestRunKey.Current, StringComparison.Ordinal))
        {
            throw new TestSafetyException(
                $"[sb-test] Отказ: база \"{databaseName}\" принадлежит другому прогону " +
                $"(ключ базы \"{ownerKey}\", ключ текущего прогона \"{TestRunKey.Current}\"). Ничего не удалено.");
        }
    }
}
