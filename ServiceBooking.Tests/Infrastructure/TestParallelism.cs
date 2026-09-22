namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §93.1 (Q11) — the one number the connection-budget fail-fast check
/// (§93.4, T8-P9) needs to know "how many test classes can be mid-run at once". xUnit's own parallelism
/// knob (<c>xunit.runner.json</c>'s <c>maxParallelThreads</c> / <c>-- xUnit.MaxParallelThreads=N</c>,
/// T8-P8) is read by the xUnit runner process itself, not by code running inside the test assembly, so
/// there is no in-process API to ask "what did the runner actually settle on" — this environment variable
/// is the seam: whoever sets <c>MaxParallelThreads</c> for the runner (a developer's shell, CI, or a
/// future <c>xunit.runner.json</c>) is expected to set this to the same value, or leave both alone.
///
/// Defaults to 1: with <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c> still
/// present (ARCHITECTURE_CYCLE8_PHASE2.md §98.1 — removing it is T8-P7, a separate, later step), at most
/// one test class is ever mid-run at a time regardless of core count, so 1 is not a guess but the actual
/// current value.
/// </summary>
public static class TestParallelism
{
    private const string EnvironmentVariable = "SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS";

    public static int MaxParallelThreads =>
        int.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariable), out var configured) && configured > 0
            ? configured
            : 1;
}
