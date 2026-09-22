using System.Text.Json;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §93.1 (Q11) — the one number the connection-budget fail-fast check
/// (§93.4, T8-P9) needs to know "how many test classes can be mid-run at once". xUnit's own parallelism
/// knob (<c>xunit.runner.json</c>'s <c>maxParallelThreads</c> / <c>-- xUnit.MaxParallelThreads=N</c>,
/// T8-P8) is read by the xUnit runner process itself, not by code running inside the test assembly, so
/// there is no in-process API to ask "what did the runner actually settle on" (confirmed against
/// xunit.runner.visualstudio 2.5.3 — RunSettings passed via <c>-- xUnit.MaxParallelThreads=N</c> never
/// reach the test host as an environment variable or any other in-process-readable value). Three
/// independent places therefore have to agree on the same number by convention, not by one of them
/// deriving from another at runtime:
///   1. <c>xunit.runner.json</c>'s <c>maxParallelThreads</c> (the local default, 4 — §93.1);
///   2. <c>-- xUnit.MaxParallelThreads=N</c> on the command line (CI's override, 2 — ci.yml);
///   3. <see cref="EnvironmentVariable"/>, which THIS class reads.
///
/// This class cannot verify (1) matches what the runner actually used, or that (2) was even passed — it
/// can only compare what IT sees against (1), and say so loudly, so a drift shows up as visible console
/// output on every single run instead of a silent, wrong connection-budget calculation:
///   - unset: falls back to <c>xunit.runner.json</c>'s own <c>maxParallelThreads</c> (single source,
///     nothing to keep in sync — the common case for a local `dotnet test`/Rider run with no override);
///   - set: used as-is (this is how a `-- xUnit.MaxParallelThreads=N` override, e.g. CI's 2, gets
///     reflected here — see ci.yml), but a console line is printed unconditionally, at whatever value,
///     so anyone reading the run's output — not just the failure case — sees the number this process
///     believes it is running at and can catch a stale override by eye.
/// </summary>
public static class TestParallelism
{
    private const string EnvironmentVariable = "SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS";
    private const int RunnerJsonFallback = 4; // kept in sync with xunit.runner.json's maxParallelThreads by convention (§93.1); see ReadRunnerJsonDefault.

    private static readonly Lazy<int> RunnerJsonDefault = new(ReadRunnerJsonDefault);
    private static int _printed; // ensures the visibility line below prints once per process, not once per read.

    public static int MaxParallelThreads
    {
        get
        {
            var envValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (int.TryParse(envValue, out var configured) && configured > 0)
            {
                if (Interlocked.Exchange(ref _printed, 1) == 0)
                {
                    Console.WriteLine(
                        $"[sb-test] {EnvironmentVariable}={configured} (explicit override — must match whatever " +
                        $"-- xUnit.MaxParallelThreads value, if any, was actually passed to this run; " +
                        $"xunit.runner.json's own default is {RunnerJsonDefault.Value}).");
                }

                return configured;
            }

            return RunnerJsonDefault.Value;
        }
    }

    /// <summary>Reads <c>maxParallelThreads</c> straight out of the <c>xunit.runner.json</c> that ships
    /// next to this test assembly (CopyToOutputDirectory), instead of hardcoding a second copy of the
    /// number here — one fewer place that can silently drift from the file xUnit itself reads. Falls
    /// back to <see cref="RunnerJsonFallback"/> only if the file is missing or unparsable, which should
    /// not happen in a normal build; that fallback is logged so it is never a silent guess either.</summary>
    private static int ReadRunnerJsonDefault()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"[sb-test] xunit.runner.json not found at {path}; falling back to {RunnerJsonFallback}.");
                return RunnerJsonFallback;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("maxParallelThreads", out var value) && value.TryGetInt32(out var parsed) && parsed > 0)
                return parsed;

            Console.WriteLine($"[sb-test] xunit.runner.json has no usable maxParallelThreads; falling back to {RunnerJsonFallback}.");
            return RunnerJsonFallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.WriteLine($"[sb-test] Could not read xunit.runner.json ({ex.Message}); falling back to {RunnerJsonFallback}.");
            return RunnerJsonFallback;
        }
    }
}
