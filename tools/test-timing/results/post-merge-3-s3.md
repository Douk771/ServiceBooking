# Замер времени регресса: post-merge-3-s3

Коммит `18c3dfd2f9ae`, 2026-10-01T17:01:28Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **5:36.1**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 5:36.1 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 5:36.1.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.1 s | template-ready |
| Создание баз классов | 1:12.0 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 32.2 s суммарно | class-db-dropped |
| Старты хоста | 4:22.4 суммарно, 202 шт. | медиана 1260 мс, p95 2747 мс |
| Тесты (wall набора) | 5:36.1 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (4:00.4), хвост 2.4 s.

CPU контейнера Postgres: макс 140 %, среднее 51 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:36.1 |
| DemoHostFactory | demo | 29 | 36.9 s |
| LegalDocumentsTestFactory | legal | 13 | 10.9 s |
| NotificationDispatchTestFactory | dispatch | 6 | 8.0 s |
| PushEnabledFactory | api | 8 | 7.6 s |
| RateLimitTestFactory | ratelimit | 8 | 6.3 s |
| ChannelHookFactory | ntf | 5 | 5.3 s |
| StaffMaxTestFactory | api | 5 | 4.6 s |
| NotificationTestFactory | ntf | 5 | 4.5 s |
| TrialDispatchFactory | dispatch | 2 | 4.1 s |
| CompanyAddressTestFactory | addr | 3 | 4.0 s |
| PhoneVerificationEnabledFactory | api | 5 | 3.9 s |
| CatalogTestFactory | api | 3 | 3.8 s |
| ProdLimitsHost | api | 5 | 3.6 s |
| PushDispatchTestFactory | dispatch | 2 | 1.6 s |
| TightHost | api | 1 | 0.7 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.6 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 36.9 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 6.9 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.9 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 5.6 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 5.3 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 4.0 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 5.2 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 5.1 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 6.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 5.6 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 4.2 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 3.5 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 3.3 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 2.4 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 2.1 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 1.1 s |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 2 | 4.2 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:13.1 | 3:34.2 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 4:03.4 | 4:00.4 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:46.1 | 3:43.3 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:23.6 | 2:59.6 | 3 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:20.8 | 2:13.8 | 21 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:19.2 | 1:22.8 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:04.3 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 1:48.1 | 1:47.2 | 51 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:30.3 | 1:25.7 | 16 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:29.1 | 1:26.2 | 2 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:23.0 | 1:17.2 | 15 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:17.3 | 1:06.6 | 12 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 1:15.2 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:13.0 | 1:08.7 | 18 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:12.1 | 56.7 s | 11 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:11.4 | 1:05.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:10.7 | 1:07.7 | 28 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:10.0 | 1:06.0 | 36 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:09.8 | 1:02.9 | 21 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:09.2 | 0.0 s | 1 |
