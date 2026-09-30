namespace ServiceBooking.API.Services.Ops;

public enum OpsAction
{
    TariffsPlan,
    TariffsApply,
    ShowcasePlan,
    ShowcaseCreate,
    ShowcaseRecreate,
    ShowcaseDelete,
    DemoReset,
}

/// <summary>What <c>ops showcase plan</c> reports on. Null in <see cref="OpsCommandLine.PlanOf"/> means "whatever makes sense now".</summary>
public enum ShowcasePlanKind { Create, Recreate, Delete }

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.1, API_CONTRACT_CYCLE28.md §602 — the parsed form of <c>dotnet ServiceBooking.API.dll ops …</c>.
/// Pure: no I/O, no configuration. <see cref="Parse"/> returns null when the first argument is not <c>ops</c>, and then the process
/// starts as the web server exactly as before.
///
/// Tokens that look like configuration (<c>--Key=Value</c>) are not commands: they are handed to the host builder
/// (<see cref="HostArgs"/>), so that an operator can still override a setting for one run. <c>--yes</c> confirms a changing command.
/// Anything else after <c>ops</c> is either the command or an error (exit code 64, help text).
/// </summary>
public sealed record OpsCommandLine(OpsAction? Action, ShowcasePlanKind? PlanOf, bool Confirmed, string[] HostArgs, string? Error)
{
    public const int ExitUnknownCommand = 64;

    public const string Help = """
        Использование: dotnet ServiceBooking.API.dll ops <команда> [--yes]

          tariffs plan                          показать, какие тарифы «Записи» будут созданы (ничего не меняет)
          tariffs apply                         создать недостающие тарифы (существующие не трогает)
          showcase plan [create|recreate|delete]  показать, что будет создано / удалено (ничего не меняет)
          showcase create [--yes]               создать витрину (без --yes только план)
          showcase recreate [--yes]             удалить помеченное и создать заново
          showcase delete [--yes]               удалить всё помеченное как витрина, включая файлы
          demo reset [--yes]                    сбросить демо-стенд (только демо-режим)

        Коды выхода: 0 — успех; 1 — ошибка; 2 — отказ (витрина уже есть, нет города/тарифа, не демо); 3 — есть непримененные миграции;
        4 — занят замок (идёт другой запуск); 64 — неизвестная команда.
        """;

    public static OpsCommandLine? Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "ops", StringComparison.OrdinalIgnoreCase))
            return null;

        var words = new List<string>();
        var hostArgs = new List<string>();
        var confirmed = false;
        foreach (var token in args.Skip(1))
        {
            if (string.Equals(token, "--yes", StringComparison.OrdinalIgnoreCase)) confirmed = true;
            else if (token.StartsWith("--", StringComparison.Ordinal) && token.Contains('=')) hostArgs.Add(token);
            else if (token.StartsWith('-')) return Fail($"Неизвестный параметр: {token}");
            else words.Add(token.ToLowerInvariant());
        }

        return words switch
        {
            ["tariffs", "plan"] => Ok(OpsAction.TariffsPlan),
            ["tariffs", "apply"] => Ok(OpsAction.TariffsApply),
            ["showcase", "plan"] => Ok(OpsAction.ShowcasePlan),
            ["showcase", "plan", "create"] => Ok(OpsAction.ShowcasePlan, ShowcasePlanKind.Create),
            ["showcase", "plan", "recreate"] => Ok(OpsAction.ShowcasePlan, ShowcasePlanKind.Recreate),
            ["showcase", "plan", "delete"] => Ok(OpsAction.ShowcasePlan, ShowcasePlanKind.Delete),
            ["showcase", "create"] => Ok(OpsAction.ShowcaseCreate),
            ["showcase", "recreate"] => Ok(OpsAction.ShowcaseRecreate),
            ["showcase", "delete"] => Ok(OpsAction.ShowcaseDelete),
            ["demo", "reset"] => Ok(OpsAction.DemoReset),
            [] => Fail("Не указана команда."),
            _ => Fail($"Неизвестная команда: {string.Join(' ', words)}"),
        };

        OpsCommandLine Ok(OpsAction action, ShowcasePlanKind? planOf = null) => new(action, planOf, confirmed, hostArgs.ToArray(), null);
        OpsCommandLine Fail(string error) => new(null, null, confirmed, hostArgs.ToArray(), error);
    }
}
