using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ServiceBooking.TestKit;

/// <summary>
/// US-90: "what's in use" — read-only reporting, never a destructive action. No branch in this class
/// calls DROP, rm, or stop (§73.4, приёмочный критерий).
/// </summary>
public static class EnvStatus
{
    public static async Task<int> RunAsync(string[] args)
    {
        var asJson = args.Contains("--json");

        var projectName = Environment.GetEnvironmentVariable("SB_PROJECT_NAME") ?? "servicebooking";
        var dbPort = ParsePort(Environment.GetEnvironmentVariable("SB_DB_PORT"), 5432);
        var apiPort = ParsePort(Environment.GetEnvironmentVariable("SB_API_PORT"), 5000);
        var webPort = ParsePort(Environment.GetEnvironmentVariable("SB_WEB_PORT"), 5173);

        var ports = new[]
        {
            new { Name = "SB_DB_PORT", Port = dbPort, InUse = IsPortBound(dbPort) },
            new { Name = "SB_API_PORT", Port = apiPort, InUse = IsPortBound(apiPort) },
            new { Name = "SB_WEB_PORT", Port = webPort, InUse = IsPortBound(webPort) },
        };

        var dockerAvailable = await IsDockerAvailableAsync();
        var currentWorkdir = Environment.CurrentDirectory;

        List<object> containers = [];
        List<object> databases = [];

        if (dockerAvailable)
        {
            try
            {
                var psOutput = await Sweeper.RunDockerAsync(["ps", "-a", "--filter", $"label={ResourceLabels.OwnerLabel}=1", "--format", "{{json .}}"]);
                foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var doc = JsonDocument.Parse(line);
                    var id = doc.RootElement.GetProperty("ID").GetString()!;
                    var name = doc.RootElement.GetProperty("Names").GetString() ?? id;
                    var inspect = await Sweeper.RunDockerAsync($"inspect {id}");
                    using var inspectDoc = JsonDocument.Parse(inspect);
                    var labels = inspectDoc.RootElement[0].GetProperty("Config").GetProperty("Labels");
                    var workdir = labels.TryGetProperty(ResourceLabels.WorkdirLabel, out var w) ? w.GetString() : null;
                    var runKey = labels.TryGetProperty(ResourceLabels.RunKeyLabel, out var rk) ? rk.GetString() : null;
                    containers.Add(new { Name = name, RunKey = runKey, Mine = workdir == currentWorkdir });
                }
            }
            catch
            {
                // status is best-effort read-only reporting; a docker hiccup here just means an empty list.
            }
        }

        var payload = new
        {
            projectName,
            composeProjectName = projectName,
            docker = dockerAvailable ? "available" : "недоступен",
            ports,
            containers,
            databases,
        };

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"[sb-status] проект: {projectName}");
            Console.WriteLine($"[sb-status] docker: {(dockerAvailable ? "доступен" : "недоступен")}");
            foreach (var p in ports)
                Console.WriteLine($"[sb-status] {p.Name}={p.Port}  {(p.InUse ? "занят" : "свободен")}");
            if (containers.Count == 0)
                Console.WriteLine("[sb-status] тестовых контейнеров не найдено");
            foreach (dynamic c in containers)
                Console.WriteLine($"[sb-status] контейнер {c.Name}  run={c.RunKey}  {(c.Mine ? "мой" : "чужой")}");
        }

        return 0;
    }

    public static async Task<int> RunDoctorAsync(string[] args)
    {
        var dockerAvailable = await IsDockerAvailableAsync();
        Console.WriteLine(dockerAvailable
            ? "[sb-doctor] Docker доступен — прогон сможет поднять контейнер сам."
            : "[sb-doctor] Docker недоступен. Запустите Docker Desktop, либо задайте SERVICEBOOKING_TEST_CONNECTION " +
              "для запуска против внешнего сервера. Подробности: docs/testing-isolation.md");
        return dockerAvailable ? 0 : 1;
    }

    private static int ParsePort(string? raw, int fallback) => int.TryParse(raw, out var v) ? v : fallback;

    private static bool IsPortBound(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            await Sweeper.RunDockerAsync(["info", "--format", "{{.ServerVersion}}"]);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
