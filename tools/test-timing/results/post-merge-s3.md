# Замер времени регресса: post-merge-s3

Коммит `8b1d54d5d892` (dirty), 2026-10-01T15:22:17Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **9:50.0**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 9:50.0 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 9:50.0.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 4.6 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.3 s | template-ready |
| Создание баз классов | 38.1 s суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 18.6 s суммарно | class-db-dropped |
| Старты хоста | 2:33.3 суммарно, 200 шт. | медиана 629 мс, p95 1691 мс |
| Тесты (wall набора) | 9:50.0 | |

Планирование: простой потоков 0.7 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle35DemoMutationTests (4:02.2), хвост 6:10.1.

CPU контейнера Postgres: макс 126 %, среднее 38 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 1:35.1 |
| LegalDocumentsTestFactory | legal | 13 | 7.9 s |
| DemoHostFactory | demo | 27 | 7.8 s |
| PushEnabledFactory | api | 8 | 5.4 s |
| RateLimitTestFactory | ratelimit | 8 | 5.0 s |
| NotificationTestFactory | ntf | 5 | 4.9 s |
| ProdLimitsHost | api | 5 | 3.9 s |
| NotificationDispatchTestFactory | dispatch | 6 | 3.7 s |
| ChannelHookFactory | ntf | 5 | 3.6 s |
| StaffMaxTestFactory | api | 5 | 3.4 s |
| PushDispatchTestFactory | dispatch | 2 | 3.2 s |
| PhoneVerificationEnabledFactory | api | 5 | 3.0 s |
| CatalogTestFactory | api | 3 | 2.4 s |
| CompanyAddressTestFactory | addr | 3 | 1.7 s |
| TrialDispatchFactory | dispatch | 2 | 1.1 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.8 s |
| TightHost | api | 1 | 0.2 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 7.8 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 5.9 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.3 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 3.6 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 3.6 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 2.0 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 3.9 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 1.1 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 4.4 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.5 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 2.4 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 2.2 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 2.1 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 1.9 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 1.7 s |
| ServiceBooking.Tests.Tests.Cycle24PersonalDataTests | 2 | 2.9 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 2:42.6 | 1:57.8 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 2:11.4 | 2:08.6 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:53.3 | 1:51.8 | 85 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:20.5 | 1:16.5 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:16.0 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 1:14.7 | 2:11.0 | 5 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:04.0 | 55.7 s | 18 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationTests | 56.0 s | 4:02.2 | 12 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 53.6 s | 46.5 s | 19 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 50.3 s | 49.1 s | 21 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 48.6 s | 38.3 s | 11 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 48.0 s | 42.9 s | 28 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 46.5 s | 36.9 s | 18 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 46.0 s | 38.2 s | 16 |
| ServiceBooking.Tests.Tests.AdminTests | 43.5 s | 39.5 s | 51 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 42.8 s | 34.3 s | 11 |
| ServiceBooking.Tests.Tests.MastersTests | 40.8 s | 36.3 s | 19 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 40.5 s | 39.3 s | 27 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 40.5 s | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 39.6 s | 36.9 s | 36 |
