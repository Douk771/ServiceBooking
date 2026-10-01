# Замер времени регресса: post-merge-s1

Коммит `8b1d54d5d892`, 2026-10-01T15:01:57Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **10:41.2**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 10:41.2 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 10:41.2.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 8.7 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.6 s | server-ready |
| Шаблон БД (миграции) | 4.1 s | template-ready |
| Создание баз классов | 46.5 s суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 28.9 s суммарно | class-db-dropped |
| Старты хоста | 3:00.9 суммарно, 200 шт. | медиана 675 мс, p95 2186 мс |
| Тесты (wall набора) | 10:41.2 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle35DemoMutationTests (4:10.9), хвост 6:34.4.

CPU контейнера Postgres: макс 134 %, среднее 34 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 1:54.9 |
| LegalDocumentsTestFactory | legal | 13 | 12.0 s |
| DemoHostFactory | demo | 27 | 10.3 s |
| ProdLimitsHost | api | 5 | 6.3 s |
| NotificationDispatchTestFactory | dispatch | 6 | 4.8 s |
| RateLimitTestFactory | ratelimit | 8 | 4.5 s |
| NotificationTestFactory | ntf | 5 | 4.3 s |
| PushDispatchTestFactory | dispatch | 2 | 4.0 s |
| PhoneVerificationEnabledFactory | api | 5 | 3.9 s |
| StaffMaxTestFactory | api | 5 | 3.5 s |
| PushEnabledFactory | api | 8 | 3.4 s |
| CatalogTestFactory | api | 3 | 3.3 s |
| ChannelHookFactory | ntf | 5 | 2.7 s |
| CompanyAddressTestFactory | addr | 3 | 1.5 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.8 s |
| TrialDispatchFactory | dispatch | 2 | 0.7 s |
| TightHost | api | 1 | 0.2 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 10.3 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 9.7 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 8.6 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 1.3 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 2.9 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 2.7 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 2.3 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 8.1 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 4.9 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 3.8 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.1 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 2.4 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 2.1 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 1.7 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 1.3 s |
| ServiceBooking.Tests.Tests.Cycle29ContractTests | 2 | 5.0 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 2:59.9 | 2:40.8 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 1:55.3 | 1:51.7 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:45.4 | 1:43.6 | 85 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 1:23.5 | 3:31.1 | 5 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:20.3 | 1:06.3 | 18 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:18.5 | 1:04.3 | 14 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:15.7 | 1:10.5 | 19 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:11.4 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:07.6 | 1:04.6 | 11 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 1:06.1 | 55.0 s | 11 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 58.3 s | 55.5 s | 27 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationTests | 57.5 s | 4:10.9 | 12 |
| ServiceBooking.Tests.Tests.PaginationTests | 51.7 s | 47.3 s | 14 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 48.8 s | 45.1 s | 8 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 46.9 s | 40.6 s | 18 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 46.7 s | 39.0 s | 28 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 46.6 s | 42.7 s | 21 |
| ServiceBooking.Tests.Tests.Cycle31CatalogListingTests | 43.6 s | 23.3 s | 16 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 42.5 s | 35.8 s | 16 |
| ServiceBooking.Tests.Tests.MastersTests | 40.3 s | 36.0 s | 19 |
