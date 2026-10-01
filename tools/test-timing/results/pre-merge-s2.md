# Замер времени регресса: pre-merge-s2

Коммит `d64ba8a0cdb4`, 2026-10-01T17:52:39Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **5:54.5**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 5:54.5 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 5:54.5.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.4 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 1:07.5 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 35.8 s суммарно | class-db-dropped |
| Старты хоста | 4:15.7 суммарно, 202 шт. | медиана 1211 мс, p95 2715 мс |
| Тесты (wall набора) | 5:54.5 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (3:39.9), хвост 19.6 s.

CPU контейнера Postgres: макс 147 %, среднее 56 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:31.6 |
| DemoHostFactory | demo | 29 | 29.4 s |
| LegalDocumentsTestFactory | legal | 13 | 10.9 s |
| RateLimitTestFactory | ratelimit | 8 | 10.0 s |
| NotificationTestFactory | ntf | 5 | 7.9 s |
| NotificationDispatchTestFactory | dispatch | 6 | 6.9 s |
| PushEnabledFactory | api | 8 | 6.1 s |
| ProdLimitsHost | api | 5 | 5.6 s |
| ChannelHookFactory | ntf | 5 | 4.7 s |
| CatalogTestFactory | api | 3 | 4.5 s |
| StaffMaxTestFactory | api | 5 | 4.2 s |
| PhoneVerificationEnabledFactory | api | 5 | 3.8 s |
| TrialDispatchFactory | dispatch | 2 | 3.6 s |
| CompanyAddressTestFactory | addr | 3 | 2.8 s |
| PushDispatchTestFactory | dispatch | 2 | 1.9 s |
| TightHost | api | 1 | 1.2 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.5 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 29.4 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 5.8 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 7.2 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 6.9 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 5.1 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 3.7 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 4.0 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 3.5 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 1.4 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 6.5 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 5.2 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 5.1 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 4.7 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 4.4 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 3.1 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 2.4 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.1 s |
| ServiceBooking.Tests.Tests.SchedulerTests | 2 | 3.6 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:05.6 | 2:59.1 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 3:44.9 | 3:39.9 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:27.4 | 3:23.9 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:34.4 | 3:16.1 | 3 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:08.7 | 2:04.9 | 14 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:02.9 | 1:56.9 | 21 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:52.6 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:49.6 | 2:04.2 | 3 |
| ServiceBooking.Tests.Tests.AdminTests | 1:26.3 | 1:24.7 | 51 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:17.8 | 1:09.0 | 18 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:17.2 | 1:09.6 | 36 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:16.8 | 1:09.6 | 15 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:13.2 | 56.5 s | 11 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:11.5 | 1:03.0 | 16 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:09.8 | 1:05.7 | 19 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:09.4 | 1:06.0 | 16 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:08.9 | 1:07.7 | 28 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:04.4 | 58.6 s | 12 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:02.9 | 1:00.5 | 18 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 1:00.1 | 56.0 s | 27 |
