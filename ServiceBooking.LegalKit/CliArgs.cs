namespace ServiceBooking.LegalKit;

/// <summary>Minimal <c>--flag value</c> / <c>--switch</c> parser for the six commands in
/// ARCHITECTURE_CYCLE11.md §105/§117.2 — no external dependency is warranted for six known flags.</summary>
internal sealed class CliArgs
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public static CliArgs Parse(IReadOnlyList<string> args)
    {
        var result = new CliArgs();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new CliUsageException($"Неожиданный аргумент: '{arg}'.");

            var name = arg[2..];
            if (name is "dry-run" or "json")
            {
                result._flags.Add(name);
                continue;
            }

            if (i + 1 >= args.Count)
                throw new CliUsageException($"Флагу --{name} требуется значение.");
            result._values[name] = args[++i];
        }
        return result;
    }

    public string? Get(string name) => _values.GetValueOrDefault(name);
    public string GetOrDefault(string name, string fallback) => _values.GetValueOrDefault(name, fallback);
    public string Require(string name) => _values.TryGetValue(name, out var v)
        ? v
        : throw new CliUsageException($"Обязательный флаг --{name} не указан.");
    public bool Has(string flag) => _flags.Contains(flag);
}

internal sealed class CliUsageException(string message) : Exception(message);
