using Xunit.Abstractions;
using Xunit.Sdk;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// T8-P11a (US-100, ARCHITECTURE_CYCLE8_PHASE2.md §99.2): a stable test-execution order inside a class
/// lets a hidden "test B only passes after test A ran" dependency hide unnoticed forever — class-level
/// parallelism (T8-P7) only randomizes which CLASS runs when, not the order of tests WITHIN one class.
/// This orderer randomizes that order too.
///
/// Reproducibility is not optional: a random order that cannot be replayed turns a failure into a shrug.
/// The seed used on every run is:
///   1. <see cref="SeedEnvironmentVariable"/> if set — lets a CI failure or a local flake be replayed
///      exactly (<c>SERVICEBOOKING_TEST_ORDER_SEED=1234567 dotnet test ...</c>);
///   2. otherwise a fresh seed derived from <see cref="Environment.TickCount64"/>, printed to the console
///      so the run that just happened can still be replayed afterwards.
/// The seed is printed exactly once per process (xUnit constructs one orderer instance per test class),
/// not once per class, so the console isn't spammed with the same number.
/// </summary>
public sealed class RandomTestCaseOrderer : ITestCaseOrderer
{
    private const string SeedEnvironmentVariable = "SERVICEBOOKING_TEST_ORDER_SEED";

    private static readonly Lazy<int> Seed = new(ResolveSeed);
    private static int _printed;

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        if (Interlocked.Exchange(ref _printed, 1) == 0)
        {
            Console.WriteLine(
                $"[sb-test] {nameof(RandomTestCaseOrderer)} seed={Seed.Value} " +
                $"(replay this exact order with {SeedEnvironmentVariable}={Seed.Value}).");
        }

        // A seed shared across the whole run (not re-derived per class) still gives a different
        // shuffle per class, because Random.Shared-style state advances with every call to Next() —
        // but a fresh Random per class, seeded from the run seed plus a stable per-class offset, keeps
        // one class's order unaffected by how many test cases some other class in the same run has.
        var materialized = testCases.ToList();
        var classSeed = unchecked(Seed.Value * 397 ^ TestCaseClassNameHash(materialized));
        var random = new Random(classSeed);

        // Fisher-Yates: uniform, in place, O(n).
        for (var i = materialized.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (materialized[i], materialized[j]) = (materialized[j], materialized[i]);
        }

        return materialized;
    }

    private static int TestCaseClassNameHash<TTestCase>(IReadOnlyList<TTestCase> testCases) where TTestCase : ITestCase
    {
        var className = testCases.Count > 0 ? testCases[0].TestMethod.TestClass.Class.Name : string.Empty;
        return ServiceBooking.TestKit.StableHash.OfString(className);
    }

    // The per-class offset must be identical in a later process, otherwise the replay line printed
    // above is a lie. See ServiceBooking.TestKit.StableHash for why string.GetHashCode cannot be used.

    private static int ResolveSeed()
    {
        var envValue = Environment.GetEnvironmentVariable(SeedEnvironmentVariable);
        if (int.TryParse(envValue, out var configured))
            return configured;

        return unchecked((int)Environment.TickCount64);
    }
}
