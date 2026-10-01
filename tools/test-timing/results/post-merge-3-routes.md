# Замер времени регресса: post-merge-3-routes

Коммит `18c3dfd2f9ae`, 2026-10-01T17:13:15Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Примечание: route-log enabled: timings not comparable.

Итого медиана: **5:50.8**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 5:50.8 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 5:50.8.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 3.9 s | template-ready |
| Создание баз классов | 1:28.8 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 39.4 s суммарно | class-db-dropped |
| Старты хоста | 4:45.8 суммарно, 202 шт. | медиана 1391 мс, p95 2790 мс |
| Тесты (wall набора) | 5:50.8 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (4:03.8), хвост 2.6 s.

CPU контейнера Postgres: макс 166 %, среднее 52 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:42.6 |
| DemoHostFactory | demo | 29 | 37.1 s |
| LegalDocumentsTestFactory | legal | 13 | 15.6 s |
| NotificationDispatchTestFactory | dispatch | 6 | 12.3 s |
| PushEnabledFactory | api | 8 | 9.0 s |
| RateLimitTestFactory | ratelimit | 8 | 8.9 s |
| NotificationTestFactory | ntf | 5 | 8.8 s |
| ChannelHookFactory | ntf | 5 | 8.1 s |
| StaffMaxTestFactory | api | 5 | 5.3 s |
| ProdLimitsHost | api | 5 | 4.2 s |
| CompanyAddressTestFactory | addr | 3 | 3.1 s |
| CatalogTestFactory | api | 3 | 2.8 s |
| TrialDispatchFactory | dispatch | 2 | 2.3 s |
| PhoneVerificationEnabledFactory | api | 5 | 2.0 s |
| PushDispatchTestFactory | dispatch | 2 | 1.8 s |
| TightHost | api | 1 | 1.5 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.5 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 37.1 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 9.7 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.0 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 8.1 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 6.1 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 5.9 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 4.8 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 4.4 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 2.8 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 8.1 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 6.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 6.3 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 5.7 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 3.7 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 3.3 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 1.6 s |
| ServiceBooking.Tests.Tests.NotificationDispatchTests | 2 | 4.0 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:23.4 | 3:03.8 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 4:07.9 | 4:03.8 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:43.4 | 3:40.3 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:30.2 | 3:11.5 | 3 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:19.0 | 2:10.1 | 21 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:17.8 | 2:02.1 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:56.0 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 1:44.6 | 1:43.7 | 51 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:37.9 | 1:35.4 | 2 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:27.1 | 1:19.8 | 16 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:27.0 | 1:15.5 | 18 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:26.7 | 1:14.0 | 12 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:25.7 | 1:18.1 | 15 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:15.9 | 1:11.5 | 19 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 1:10.8 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:10.0 | 1:06.2 | 36 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:09.9 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:09.1 | 1:04.7 | 28 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:08.4 | 1:04.6 | 16 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:08.2 | 56.9 s | 11 |
