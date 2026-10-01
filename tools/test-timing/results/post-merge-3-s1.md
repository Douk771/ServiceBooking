# Замер времени регресса: post-merge-3-s1

Коммит `18c3dfd2f9ae`, 2026-10-01T16:48:53Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **6:27.8**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 6:27.8 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 6:27.8.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 18.4 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.6 s | server-ready |
| Шаблон БД (миграции) | 4.2 s | template-ready |
| Создание баз классов | 1:33.8 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 44.1 s суммарно | class-db-dropped |
| Старты хоста | 5:07.0 суммарно, 202 шт. | медиана 1470 мс, p95 2877 мс |
| Тесты (wall набора) | 6:27.8 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (4:34.7), хвост 2.3 s.

CPU контейнера Postgres: макс 132 %, среднее 50 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:55.8 |
| DemoHostFactory | demo | 29 | 38.3 s |
| LegalDocumentsTestFactory | legal | 13 | 16.2 s |
| NotificationDispatchTestFactory | dispatch | 6 | 11.0 s |
| NotificationTestFactory | ntf | 5 | 10.9 s |
| PushEnabledFactory | api | 8 | 9.3 s |
| RateLimitTestFactory | ratelimit | 8 | 7.7 s |
| PhoneVerificationEnabledFactory | api | 5 | 6.6 s |
| StaffMaxTestFactory | api | 5 | 6.2 s |
| ChannelHookFactory | ntf | 5 | 5.5 s |
| ProdLimitsHost | api | 5 | 4.4 s |
| CompanyAddressTestFactory | addr | 3 | 4.2 s |
| CatalogTestFactory | api | 3 | 3.7 s |
| TrialDispatchFactory | dispatch | 2 | 3.6 s |
| PushDispatchTestFactory | dispatch | 2 | 1.7 s |
| TightHost | api | 1 | 1.3 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.6 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 38.3 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 10.7 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.3 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 5.2 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 6.8 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 5.5 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 5.5 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 6.3 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 5.3 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 4.9 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 8.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 7.9 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 5.6 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 5.1 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 4.4 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 4.4 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 3.7 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 3.0 s |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 2 | 6.3 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:42.0 | 3:58.9 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 4:39.9 | 4:34.7 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 4:14.9 | 4:09.4 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:50.8 | 3:39.5 | 3 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:38.6 | 2:34.1 | 14 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:28.0 | 2:21.1 | 21 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:02.3 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 1:59.7 | 1:54.1 | 51 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:48.6 | 1:54.7 | 2 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:41.7 | 1:36.1 | 16 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:33.2 | 1:26.8 | 19 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:33.2 | 1:21.4 | 18 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:27.4 | 1:22.2 | 15 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:25.8 | 1:14.7 | 11 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:25.4 | 1:17.7 | 16 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:23.2 | 1:12.3 | 28 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:20.5 | 1:15.2 | 36 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:19.4 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:18.3 | 1:08.8 | 12 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:18.2 | 1:11.3 | 18 |
