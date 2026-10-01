# Замер времени регресса: post-merge-2-s1

Коммит `ac057263648a`, 2026-10-01T16:06:45Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **7:10.5**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 7:10.5 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:10.5.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.2 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 1:21.4 суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 29.5 s суммарно | class-db-dropped |
| Старты хоста | 4:44.2 суммарно, 200 шт. | медиана 1366 мс, p95 3040 мс |
| Тесты (wall набора) | 7:10.5 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (5:29.5), хвост 10.2 s.

CPU контейнера Postgres: макс 103 %, среднее 47 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:45.4 |
| DemoHostFactory | demo | 27 | 44.4 s |
| LegalDocumentsTestFactory | legal | 13 | 9.5 s |
| ChannelHookFactory | ntf | 5 | 9.1 s |
| PushEnabledFactory | api | 8 | 9.1 s |
| PhoneVerificationEnabledFactory | api | 5 | 6.4 s |
| RateLimitTestFactory | ratelimit | 8 | 5.6 s |
| ProdLimitsHost | api | 5 | 5.6 s |
| StaffMaxTestFactory | api | 5 | 5.1 s |
| NotificationTestFactory | ntf | 5 | 4.4 s |
| NotificationDispatchTestFactory | dispatch | 6 | 4.1 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 3.3 s |
| TrialDispatchFactory | dispatch | 2 | 3.3 s |
| CompanyAddressTestFactory | addr | 3 | 2.8 s |
| CatalogTestFactory | api | 3 | 2.6 s |
| PushDispatchTestFactory | dispatch | 2 | 2.5 s |
| TightHost | api | 1 | 1.1 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 44.4 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 6.9 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.7 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 3.8 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 9.6 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 9.1 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 2.6 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 7.5 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 3.5 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.5 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 6.6 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 4.5 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 3.5 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 2.6 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 2.6 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 0.9 s |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 2 | 5.1 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 5:32.2 | 5:29.5 | 63 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 5:11.6 | 4:30.8 | 7 |
| ServiceBooking.Tests.Tests.CompaniesTests | 5:04.3 | 5:04.6 | 85 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 3:14.6 | 3:07.4 | 14 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:50.6 | 2:45.6 | 21 |
| ServiceBooking.Tests.Tests.AdminTests | 2:08.3 | 2:02.9 | 51 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 2:06.2 | 4:42.8 | 5 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:56.8 | 1:48.1 | 28 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:49.2 | 1:45.5 | 16 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationATests | 1:49.0 | 4:53.8 | 4 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:48.7 | 1:38.3 | 16 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:48.7 | 1:35.1 | 18 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:48.3 | 1:40.6 | 15 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:45.9 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:41.3 | 1:26.5 | 12 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:40.1 | 1:36.9 | 36 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:31.5 | 1:28.0 | 28 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:25.5 | 1:18.0 | 21 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:24.9 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 1:21.0 | 1:13.5 | 12 |
