using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE36.md §36.5 — замер тестового прогона. Пишет строки JSONL в
/// <c>TestResults/sb-test-metrics-&lt;runKey&gt;.jsonl</c> (и журнал маршрутов в
/// <c>sb-test-routes-&lt;runKey&gt;.jsonl</c> при SERVICEBOOKING_TEST_ROUTE_LOG=1). Запись лучшим усилием:
/// ошибка ввода-вывода глотается и тест не роняет. Форма строк — contracts/cycle36/*.schema.json.
/// </summary>
public static class TestRunMetrics
{
    public const string MetricsEnvironmentVariable = "SERVICEBOOKING_TEST_METRICS";
    public const string RouteLogEnvironmentVariable = "SERVICEBOOKING_TEST_ROUTE_LOG";

    private static readonly object Gate = new();
    private static readonly HashSet<string> SeenRoutes = new();

    public static bool MetricsEnabled =>
        Environment.GetEnvironmentVariable(MetricsEnvironmentVariable) != "0";

    public static bool RouteLogEnabled =>
        Environment.GetEnvironmentVariable(RouteLogEnvironmentVariable) == "1";

    public static string MetricsFilePath => FilePath("sb-test-metrics");
    public static string RoutesFilePath => FilePath("sb-test-routes");

    private static string FilePath(string prefix) =>
        Path.Combine(TestInfrastructure.WorkingCopyRoot, "TestResults", $"{prefix}-{TestRunKey.Current}.jsonl");

    public static void RunStart(string mode, int parallel) =>
        Write("run-start", w =>
        {
            w.WriteString("mode", mode);
            w.WriteNumber("parallel", parallel);
            w.WriteNumber("pid", Environment.ProcessId);
        });

    public static void Timed(string eventName, double ms) =>
        Write(eventName, w => w.WriteNumber("ms", Round(ms)));

    public static void ClassDb(string eventName, string slot, double ms) =>
        Write(eventName, w =>
        {
            w.WriteString("slot", slot);
            w.WriteNumber("ms", Round(ms));
        });

    public static void ClassRecorded(string slot, string testClass) =>
        Write("class-recorded", w =>
        {
            w.WriteString("slot", slot);
            w.WriteString("testClass", testClass);
        });

    public static void Host(string eventName, string slot, string factoryTag, string factoryType, double ms) =>
        Write(eventName, w =>
        {
            w.WriteString("slot", slot);
            w.WriteString("factoryTag", factoryTag);
            w.WriteString("factoryType", factoryType);
            w.WriteNumber("ms", Round(ms));
        });

    /// <summary>Одна строка на уникальную четвёрку (slot, method, route, status) в пределах процесса.</summary>
    public static void RouteHit(string slot, string method, string route, int status)
    {
        if (!RouteLogEnabled)
            return;

        lock (Gate)
        {
            if (!SeenRoutes.Add($"{slot}|{method}|{route}|{status}"))
                return;
        }

        AppendLine(RoutesFilePath, w =>
        {
            w.WriteNumber("v", 1);
            w.WriteString("runKey", TestRunKey.Current);
            w.WriteString("slot", slot);
            w.WriteString("method", method);
            w.WriteString("route", route);
            w.WriteNumber("status", status);
        });
    }

    /// <summary>Регистрирует наблюдение за запуском хоста и (по флагу) журнал маршрутов. Не меняет конвейер продукта.</summary>
    internal static void Instrument(IWebHostBuilder builder, string slot, string factoryTag, string factoryType)
    {
        var stopwatch = Stopwatch.StartNew();
        builder.ConfigureTestServices(services =>
        {
            if (MetricsEnabled)
                services.AddSingleton<IHostedService>(sp =>
                    new HostBootObserver(sp.GetRequiredService<IHostApplicationLifetime>(), stopwatch, slot, factoryTag, factoryType));

            if (RouteLogEnabled)
                services.AddTransient<IStartupFilter>(_ => new RouteLogStartupFilter(slot));
        });
    }

    private static double Round(double ms) => Math.Round(ms, 1);

    private static void Write(string eventName, Action<Utf8JsonWriter> extra)
    {
        if (!MetricsEnabled)
            return;

        AppendLine(MetricsFilePath, w =>
        {
            w.WriteNumber("v", 1);
            w.WriteString("event", eventName);
            w.WriteString("runKey", TestRunKey.Current);
            w.WriteString("utc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            extra(w);
        });
    }

    private static void AppendLine(string path, Action<Utf8JsonWriter> body)
    {
        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                body(writer);
                writer.WriteEndObject();
            }

            buffer.WriteByte((byte)'\n');
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                file.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }
        catch (Exception)
        {
            // Замер — диагностика, а не часть теста: ошибка записи не должна ронять прогон.
        }
    }

    private sealed class HostBootObserver : IHostedService
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Stopwatch _stopwatch;
        private readonly string _slot;
        private readonly string _tag;
        private readonly string _type;

        public HostBootObserver(IHostApplicationLifetime lifetime, Stopwatch stopwatch, string slot, string tag, string type)
        {
            _lifetime = lifetime;
            _stopwatch = stopwatch;
            _slot = slot;
            _tag = tag;
            _type = type;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _lifetime.ApplicationStarted.Register(() => Host("host-booted", _slot, _tag, _type, _stopwatch.Elapsed.TotalMilliseconds));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RouteLogStartupFilter(string slot) : IStartupFilter
    {
        // Наблюдающий middleware обёртывает весь конвейер и читает endpoint ПОСЛЕ ответа: к этому моменту
        // маршрутизация уже отработала, а ответ и порядок middleware продукта не затронуты.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                await pipeline();
                if (context.GetEndpoint() is RouteEndpoint endpoint)
                    RouteHit(slot, context.Request.Method, endpoint.RoutePattern.RawText ?? string.Empty, context.Response.StatusCode);
            });
            next(app);
        };
    }
}
