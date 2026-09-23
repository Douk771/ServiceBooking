using ServiceBooking.LegalKit;
using ServiceBooking.LegalKit.Commands;

// ARCHITECTURE_CYCLE11.md §104.1: this is the whole entry point — no host, no DI container, no
// Program.cs-style fail-fast configuration checks. Six commands, six exit-code contracts (§117.3).
if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
CliArgs parsed;
try
{
    parsed = CliArgs.Parse(args.Skip(1).ToArray());
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 1;
}

try
{
    return command switch
    {
        "build" => BuildCommand.Run(parsed),
        "check" => CheckCommand.Run(parsed),
        "links" => LinksCommand.Run(parsed),
        "status" => StatusCommand.Run(parsed),
        "publish" => PublishCommand.Run(parsed),
        "rollback" => RollbackCommand.Run(parsed),
        _ => UnknownCommand(command),
    };
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Неизвестная команда: '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Использование: ServiceBooking.LegalKit <build|check|links|status|publish|rollback> [флаги]

          build    --source <dir> --out <dir> [--dry-run]
          check    --source <dir> --root <dir>
          links    --root <dir>
          status   --root <dir> [--source <dir>] [--values <file>] [--json]
          publish  --source <dir> --out <dir> --values <file> --version <строка> --effective-from <ГГГГ-ММ-ДД> [--dry-run]
          rollback --out <dir> [--dry-run]

        Коды возврата: 0 успех, 1 ошибка использования, 3 расхождение артефакта (check),
        4 не готово к публикации (status), 5 публикация отклонена (publish), 6 битая ссылка/якорь (links, check).
        См. ARCHITECTURE_CYCLE11.md §105 и API_CONTRACT_CYCLE11.md §117.
        """);
}
