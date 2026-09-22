using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Покрывает чистую функцию <see cref="EnvStatus.ClassifyContainerLiveness"/> и, по построению, гарантирует
/// её совпадение с конъюнкцией мёртвости из <see cref="Sweeper"/> (review blocker: status и sweep раньше
/// расходились в классификации одного и того же ресурса при hostPidAlive == false/null и малом возрасте).
/// </summary>
public class EnvStatusClassifyContainerLivenessTests
{
    private static readonly int DefaultMaxAgeSeconds = (int)TestInfrastructure.DefaultSweepMaxAge.TotalSeconds;

    [Fact]
    public void Alive_process_is_always_alive_regardless_of_age()
    {
        EnvStatus.ClassifyContainerLiveness(hostPidAlive: true, ageSeconds: 0).Should().Be("alive");
        EnvStatus.ClassifyContainerLiveness(hostPidAlive: true, ageSeconds: DefaultMaxAgeSeconds + 1).Should().Be("alive");
    }

    [Fact]
    public void Dead_process_past_max_age_is_dead()
    {
        EnvStatus.ClassifyContainerLiveness(hostPidAlive: false, ageSeconds: DefaultMaxAgeSeconds + 1).Should().Be("dead");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(null, 0)]
    public void Young_not_provably_alive_process_is_undetermined_not_alive(bool? hostPidAlive, int ageSeconds)
    {
        // Review blocker: this used to return "alive" for a low-age unknown-pid container, disagreeing
        // with Sweeper (which would classify the identical resource as "undetermined").
        EnvStatus.ClassifyContainerLiveness(hostPidAlive, ageSeconds).Should().Be("undetermined");
    }

    [Fact]
    public void Old_process_with_unknown_liveness_is_treated_as_dead_same_as_Sweeper()
    {
        // Matches Sweeper's conjunction exactly: !processAlive && age > maxAge => dead, and
        // "processAlive" is false whenever hostPidAlive isn't literally true (including null/unknown).
        EnvStatus.ClassifyContainerLiveness(hostPidAlive: null, ageSeconds: int.MaxValue).Should().Be("dead");
    }
}
