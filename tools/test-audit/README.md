# tools/test-audit — ревизия тестов (цикл 36)

Только `python3` (стандартная библиотека). Контракт: `API_CONTRACT_CYCLE36.md` §36.26–§36.29.
Коды выхода: 0 успех, 1 аргументы, 2 среда не готова, 4 нарушение сверки.

| Скрипт | Что делает |
|---|---|
| `inventory.py --suite {unit,functional,vitest} --out F.json` | Инвентарь набора: ключ, TestCase-ID, файл, число запусков, области, защищённая зона. Проекты должны быть собраны (`dotnet build`); для vitest нужен `frontend/node_modules`. Форма — `contracts/cycle36/test-inventory.schema.json` |
| `diff_inventory.py --suite S --before B --after A --registry TEST_CATALOG.md` | Проверяет «после = до − реестр». Печатает: исчезло без записи, появилось без записи, изменилось `cases` без записи, удалён защищённый, ошибки разбора реестра. Любой непустой список — код 4 |
| `route_coverage.py --golden Cycle22RouteTable.golden.txt --before J... --after J...` | По журналам `sb-test-routes-*.jsonl` (`SERVICEBOOKING_TEST_ROUTE_LOG=1`) показывает покрытые до/после, потерянные (код 4) и никогда не покрытые маршруты эталона |

Порядок (QA): `before-*.json` снимается на коммите инструментирования до любых удалений; после ревизии — `after-*.json` и
сверка. Снимки — в `tools/test-audit/results/` (в git). Журнал маршрутов:
`SERVICEBOOKING_TEST_ROUTE_LOG=1 dotnet test ServiceBooking.Tests --no-build`, файлы `TestResults/sb-test-routes-<runKey>.jsonl`.

Тесты, пришедшие слиянием `develop`, записываются строками `R36-Mnnn` с действием «пришло мерджем develop»: колонка «Тест» — класс или файл, строка закрывает все новые и изменившиеся по `cases` тесты под этим префиксом, но не исчезнувший тест.

Сопоставление .NET-тестов между снимками идёт по `TestCase`-ID, поэтому перенос теста в другой класс (разбиение классов) не
требует строки реестра; vitest — по ключу `<файл>::<describe > it>`. «Правило по утверждению» защищённых зон (401/402/403/429/451)
скрипты не проверяют: это делает человек.
