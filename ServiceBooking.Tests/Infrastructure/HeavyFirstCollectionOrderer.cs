using Xunit.Abstractions;
using Xunit.Sdk;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Цикл 36 (BE-36-03): коллекции, которые заведомо идут дольше всех (демо-стенд и три класса генератора витрины: по ~10 тысяч записей
/// на каждый сброс или создание), стартуют первыми. Без этого xUnit запускает их в порядке обнаружения, они попадают в середину
/// прогона и становятся его хвостом: остальные потоки к этому времени уже простаивают.
/// Порядок остальных коллекций не меняется; порядок тестов внутри класса — по-прежнему <see cref="RandomTestCaseOrderer"/>.
/// Это только подсказка порядка: на корректность и изоляцию не влияет.
/// </summary>
public sealed class HeavyFirstCollectionOrderer : ITestCollectionOrderer
{
    /// <summary>Явно названная коллекция (общая база демо-стенда).</summary>
    private const string DemoCollection = "Cycle28Demo";

    /// <summary>Классы генератора витрины: у каждого своя коллекция по умолчанию, её имя содержит имя класса.</summary>
    private static readonly string[] HeavyClasses = ["Cycle28ShowcaseLifecycleTests", "Cycle28ShowcaseFreshDeleteTests", "Cycle28ShowcaseContentTests"];

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var all = testCollections.ToList();
        var heavy = all.Where(c => c.DisplayName == DemoCollection).ToList();
        foreach (var name in HeavyClasses)
            heavy.AddRange(all.Where(c => c.DisplayName.Contains(name, StringComparison.Ordinal)));
        return heavy.Concat(all.Where(c => !heavy.Contains(c)));
    }
}
