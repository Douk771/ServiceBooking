namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Цикл 36, рычаг L0 (BE-36-03): реальный предел числа одновременно живых тестовых классов.
///
/// xUnit v2 ограничивает <c>maxParallelThreads</c> только число потоков синхронизационного контекста, а не число
/// классов в работе: стоит классу дойти до <c>await</c> (создание базы, старт хоста, HTTP-вызов), его поток
/// освобождается и xUnit запускает следующую коллекцию. В замере b1 это давало до 65 одновременно живых баз и
/// очередь на клонирование шаблона (медиана 10,8 с), хотя CPU делили всего 4 потока.
///
/// Слот берётся в <see cref="TestDatabaseFixture.InitializeAsync"/> (до аренды базы) и возвращается в
/// <see cref="TestDatabaseFixture.DisposeAsync"/> (после удаления базы), то есть класс занимает слот всё время жизни
/// своей базы. Ожидание асинхронное и потоков не держит.
///
/// Предел по умолчанию — <see cref="DefaultFactor"/> × <see cref="TestParallelism.MaxParallelThreads"/>, а не ровно P:
/// тест почти всё время ждёт ответа Postgres в виртуальной машине, и ровно P классов недогружают процессор (замер
/// A/B без Cycle28, P=4: без предела 140 с, предел 4 — 138 с, 8 — 119 с, 12 — 115 с, 16 — 110/118 с, 24 — 127 с,
/// 32 — 136 с). Переопределяется <c>SERVICEBOOKING_TEST_CLASS_CONCURRENCY</c> (целое > 0).
/// Изоляция «своя база на класс» и порядок тестов внутри класса не затрагиваются.
/// </summary>
public static class ClassConcurrencyGate
{
    public const int DefaultFactor = 3;

    private const string EnvironmentVariable = "SERVICEBOOKING_TEST_CLASS_CONCURRENCY";

    private static readonly SemaphoreSlim Slots = new(Limit(), int.MaxValue);

    private static int Limit() =>
        int.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariable), out var configured) && configured > 0
            ? configured
            : TestParallelism.MaxParallelThreads * DefaultFactor;

    public static Task EnterAsync() => Slots.WaitAsync();

    public static void Exit() => Slots.Release();
}
