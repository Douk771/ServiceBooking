# Замер времени регресса: post-merge-2-s3

Коммит `ac057263648a`, 2026-10-01T16:21:21Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **7:06.9**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 7:06.9 | 1299 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:06.9.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 1:15.1 суммарно, 139 шт. | class-db-created |
| Удаление баз классов | 32.3 s суммарно | class-db-dropped |
| Старты хоста | 4:46.7 суммарно, 200 шт. | медиана 1383 мс, p95 3245 мс |
| Тесты (wall набора) | 7:06.9 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (5:23.5), хвост 10.8 s.

CPU контейнера Postgres: макс 144 %, среднее 52 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:48.0 |
| DemoHostFactory | demo | 27 | 41.1 s |
| LegalDocumentsTestFactory | legal | 13 | 12.8 s |
| ChannelHookFactory | ntf | 5 | 9.8 s |
| PushEnabledFactory | api | 8 | 8.8 s |
| StaffMaxTestFactory | api | 5 | 6.2 s |
| NotificationTestFactory | ntf | 5 | 6.0 s |
| TrialDispatchFactory | dispatch | 2 | 5.0 s |
| PhoneVerificationEnabledFactory | api | 5 | 4.8 s |
| RateLimitTestFactory | ratelimit | 8 | 4.7 s |
| ProdLimitsHost | api | 5 | 4.6 s |
| NotificationDispatchTestFactory | dispatch | 6 | 3.7 s |
| CompanyAddressTestFactory | addr | 3 | 3.3 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 2.2 s |
| PushDispatchTestFactory | dispatch | 2 | 2.1 s |
| TightHost | api | 1 | 1.9 s |
| CatalogTestFactory | api | 3 | 1.5 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 27 | 41.1 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 9.6 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 5.1 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 1.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 9.8 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 7.3 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 4.7 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 3.9 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.8 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 8.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 8.0 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 5.5 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 5.0 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 4.1 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.6 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 2.3 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 0.8 s |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 2 | 4.9 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 5:25.5 | 5:23.5 | 63 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 5:19.7 | 4:37.8 | 7 |
| ServiceBooking.Tests.Tests.CompaniesTests | 4:59.8 | 4:58.3 | 85 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 3:17.3 | 2:48.4 | 14 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:49.7 | 2:44.6 | 21 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 2:44.7 | 4:39.5 | 5 |
| ServiceBooking.Tests.Tests.Cycle35DemoMutationATests | 2:08.3 | 3:53.2 | 4 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:06.4 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 2:04.9 | 1:58.0 | 51 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:59.0 | 1:52.8 | 15 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:52.5 | 1:44.7 | 28 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:46.6 | 1:35.3 | 18 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:44.0 | 1:30.2 | 16 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:41.1 | 1:37.6 | 16 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:40.0 | 1:29.9 | 36 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:38.5 | 1:25.8 | 12 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:28.4 | 1:23.3 | 28 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 1:19.7 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 1:17.5 | 1:02.9 | 12 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:14.8 | 1:06.3 | 21 |
