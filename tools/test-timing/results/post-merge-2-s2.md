# Замер времени регресса: post-merge-2-s2

Коммит `ac057263648a`, 2026-10-01T16:13:59Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **7:18.4**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 7:18.4 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:18.4.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 3.8 s | template-ready |
| Создание баз классов | 1:12.8 суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 30.9 s суммарно | class-db-dropped |
| Старты хоста | 4:56.3 суммарно, 200 шт. | медиана 1412 мс, p95 3268 мс |
| Тесты (wall набора) | 7:18.4 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (5:44.8), хвост 3.1 s.

CPU контейнера Postgres: макс 151 %, среднее 55 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:52.4 |
| DemoHostFactory | demo | 27 | 43.5 s |
| ChannelHookFactory | ntf | 5 | 11.9 s |
| LegalDocumentsTestFactory | legal | 13 | 10.5 s |
| PushEnabledFactory | api | 8 | 9.7 s |
| NotificationTestFactory | ntf | 5 | 7.8 s |
| RateLimitTestFactory | ratelimit | 8 | 7.3 s |
| PhoneVerificationEnabledFactory | api | 5 | 5.2 s |
| NotificationDispatchTestFactory | dispatch | 6 | 5.2 s |
| TrialDispatchFactory | dispatch | 2 | 4.2 s |
| CompanyAddressTestFactory | addr | 3 | 4.2 s |
| StaffMaxTestFactory | api | 5 | 3.6 s |
| PushDispatchTestFactory | dispatch | 2 | 3.3 s |
| ProdLimitsHost | api | 5 | 2.5 s |
| CatalogTestFactory | api | 3 | 2.4 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 1.6 s |
| TightHost | api | 1 | 1.1 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 43.5 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 5.9 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 4.2 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.0 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 11.9 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 7.2 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 4.5 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 4.7 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.8 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 7.6 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 7.0 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 4.1 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 3.8 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 3.8 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 3.3 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 1.8 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 1.0 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 2 | 6.2 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 5:45.6 | 5:44.8 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 5:27.8 | 5:27.4 | 85 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 5:16.2 | 4:37.9 | 7 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 3:41.7 | 3:13.5 | 14 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 3:00.1 | 2:54.3 | 21 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 2:43.7 | 4:46.7 | 5 |
| ServiceBooking.Tests.Tests.AdminTests | 2:10.6 | 2:10.6 | 51 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 2:10.5 | 2:04.8 | 28 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:07.8 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 2:04.8 | 1:48.3 | 16 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 2:04.7 | 1:59.9 | 15 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:59.3 | 1:44.4 | 18 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:53.5 | 1:45.3 | 36 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationATests | 1:53.1 | 3:49.9 | 4 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:48.1 | 1:31.4 | 12 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:46.1 | 1:41.9 | 16 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:37.8 | 1:31.3 | 28 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 1:31.2 | 1:07.7 | 12 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:23.0 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:21.0 | 1:08.6 | 21 |
