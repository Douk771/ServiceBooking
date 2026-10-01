# Замер времени регресса: pre-merge-s1

Коммит `d64ba8a0cdb4`, 2026-10-01T17:45:56Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **6:37.5**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 6:37.5 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 6:37.5.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 3.4 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.7 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 1:15.9 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 44.7 s суммарно | class-db-dropped |
| Старты хоста | 5:00.0 суммарно, 202 шт. | медиана 1430 мс, p95 2984 мс |
| Тесты (wall набора) | 6:37.5 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (3:49.6), хвост 34.0 s.

CPU контейнера Postgres: макс 155 %, среднее 56 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:53.6 |
| DemoHostFactory | demo | 29 | 31.7 s |
| LegalDocumentsTestFactory | legal | 13 | 15.2 s |
| PushEnabledFactory | api | 8 | 11.7 s |
| RateLimitTestFactory | ratelimit | 8 | 10.1 s |
| NotificationDispatchTestFactory | dispatch | 6 | 8.8 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 7.0 s |
| NotificationTestFactory | ntf | 5 | 6.6 s |
| ChannelHookFactory | ntf | 5 | 6.2 s |
| StaffMaxTestFactory | api | 5 | 5.4 s |
| PhoneVerificationEnabledFactory | api | 5 | 4.8 s |
| CompanyAddressTestFactory | addr | 3 | 4.2 s |
| ProdLimitsHost | api | 5 | 4.1 s |
| TrialDispatchFactory | dispatch | 2 | 3.8 s |
| PushDispatchTestFactory | dispatch | 2 | 3.0 s |
| CatalogTestFactory | api | 3 | 2.6 s |
| TightHost | api | 1 | 1.3 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 31.7 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 7.1 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 8.0 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.0 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 8.1 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 6.2 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 4.0 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 5.3 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 5.0 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.1 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 6.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 5.8 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 4.5 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 4.4 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 2.9 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 2.8 s |
| ServiceBooking.Tests.Tests.UploadsStaticFilesTests | 2 | 7.0 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:13.0 | 3:37.5 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 3:53.6 | 3:49.6 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:31.7 | 3:31.2 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:48.3 | 3:43.8 | 3 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:14.4 | 1:27.8 | 14 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 2:07.7 | 2:23.4 | 3 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:03.6 | 1:57.4 | 21 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:58.0 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:42.2 | 1:33.8 | 18 |
| ServiceBooking.Tests.Tests.AdminTests | 1:24.9 | 1:23.2 | 51 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:21.8 | 1:16.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:17.9 | 1:13.0 | 11 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:17.0 | 1:13.5 | 36 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:15.1 | 1:08.7 | 15 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:13.4 | 1:07.4 | 12 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:12.7 | 1:07.5 | 16 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:12.2 | 1:08.0 | 16 |
| ServiceBooking.Tests.Tests.MastersTests | 1:09.5 | 1:03.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:09.5 | 1:06.1 | 28 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 1:06.9 | 59.1 s | 18 |
