using Xunit.Abstractions;
using Xunit.Sdk;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Цикл 36 (BE-36-03): коллекции, которые заведомо идут дольше всех (демо-стенд и генератор витрины: по ~10 тысяч записей
/// на каждый сброс), стартуют первыми. Без этого xUnit запускает их в порядке обнаружения, они попадают в середину
/// прогона и становятся его хвостом: остальные потоки к этому времени уже простаивают.
/// Порядок остальных коллекций не меняется; порядок тестов внутри класса — по-прежнему <see cref="RandomTestCaseOrderer"/>.
/// Это только подсказка порядка: на корректность и изоляцию не влияет.
/// </summary>
public sealed class HeavyFirstCollectionOrderer : ITestCollectionOrderer
{
    private static readonly string[] HeavyCollections = ["Cycle28Demo", "Cycle28Generator"];

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var all = testCollections.ToList();
        var heavy = HeavyCollections
            .Select(name => all.FirstOrDefault(c => c.DisplayName == name))
            .Where(c => c is not null)
            .Cast<ITestCollection>()
            .ToList();
        return heavy.Concat(all.Where(c => !heavy.Contains(c)));
    }
}
