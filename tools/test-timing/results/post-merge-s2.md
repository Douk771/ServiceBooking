# Замер времени регресса: post-merge-s2

Коммит `8b1d54d5d892`, 2026-10-01T15:12:48Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **9:24.2**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 9:24.2 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 9:24.2.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.0 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.2 s | template-ready |
| Создание баз классов | 30.3 s суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 19.6 s суммарно | class-db-dropped |
| Старты хоста | 2:28.4 суммарно, 200 шт. | медиана 641 мс, p95 1538 мс |
| Тесты (wall набора) | 9:24.2 | |

Планирование: простой потоков 3.5 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle35DemoMutationTests (3:41.2), хвост 6:02.7.

CPU контейнера Postgres: макс 145 %, среднее 35 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 1:31.3 |
| DemoHostFactory | demo | 27 | 8.8 s |
| LegalDocumentsTestFactory | legal | 13 | 8.0 s |
| ProdLimitsHost | api | 5 | 4.9 s |
| NotificationDispatchTestFactory | dispatch | 6 | 4.8 s |
| PushEnabledFactory | api | 8 | 4.8 s |
| NotificationTestFactory | ntf | 5 | 4.7 s |
| PhoneVerificationEnabledFactory | api | 5 | 4.1 s |
| RateLimitTestFactory | ratelimit | 8 | 4.0 s |
| StaffMaxTestFactory | api | 5 | 2.5 s |
| ChannelHookFactory | ntf | 5 | 2.1 s |
| CatalogTestFactory | api | 3 | 2.0 s |
| CompanyAddressTestFactory | addr | 3 | 1.7 s |
| PushDispatchTestFactory | dispatch | 2 | 1.6 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 1.3 s |
| TrialDispatchFactory | dispatch | 2 | 1.3 s |
| TightHost | api | 1 | 0.6 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 8.8 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.2 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 1.3 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 3.8 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 2.1 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.1 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 3.2 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 3.0 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.6 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 2.5 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 2.1 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 1.9 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 1.8 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 1.6 s |
| ServiceBooking.Tests.Tests.StaffPushSubscriptionAndQueueingTests | 2 | 2.9 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 2:30.6 | 2:10.8 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 1:56.3 | 1:55.0 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:43.5 | 1:41.9 | 85 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:17.3 | 1:15.2 | 14 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 1:14.9 | 1:59.5 | 5 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:09.3 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 58.5 s | 49.7 s | 18 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationTests | 56.7 s | 3:41.2 | 12 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 53.2 s | 50.8 s | 19 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 48.9 s | 45.5 s | 11 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 46.0 s | 42.4 s | 21 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 44.6 s | 41.7 s | 28 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 44.6 s | 36.1 s | 11 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 42.8 s | 38.1 s | 16 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 40.2 s | 36.0 s | 18 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 40.0 s | 38.6 s | 27 |
| ServiceBooking.Tests.Tests.AdminTests | 38.6 s | 35.8 s | 51 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 37.8 s | 35.5 s | 36 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 37.1 s | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 37.0 s | 30.6 s | 8 |
