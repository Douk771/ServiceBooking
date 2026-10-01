using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA цикл 25 — вторичный хост против БД класса с ВЫКЛЮЧЕННЫМ кешем каталога goods (<c>Orders:CatalogCacheSeconds = 0</c>). Обычный хост держит список
/// города 30 секунд (API_CONTRACT_CYCLE25.md §531), и тест, который меняет условия видимости и тут же читает каталог, иначе читал бы вчерашний ответ.
/// Сам кеш (до 30 с) — задокументированное поведение, а не предмет этих сценариев.
/// </summary>
public sealed class CatalogTestFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestHostSettings.Apply(builder, "api", connectionString, factoryType: GetType().Name);
        builder.UseSetting("Orders:CatalogCacheSeconds", "0");
    }
}
