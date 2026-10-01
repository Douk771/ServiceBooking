# Замер времени регресса: post-merge-3-s2b

Коммит `18c3dfd2f9ae`, 2026-10-01T17:07:23Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **5:38.2**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 5:38.2 | 1300 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 5:38.2.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 1:13.6 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 33.8 s суммарно | class-db-dropped |
| Старты хоста | 4:27.9 суммарно, 202 шт. | медиана 1282 мс, p95 2659 мс |
| Тесты (wall набора) | 5:38.2 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (3:58.3), хвост 2.2 s.

CPU контейнера Postgres: макс 165 %, среднее 56 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:36.7 |
| DemoHostFactory | demo | 29 | 33.9 s |
| LegalDocumentsTestFactory | legal | 13 | 10.8 s |
| NotificationDispatchTestFactory | dispatch | 6 | 8.8 s |
| NotificationTestFactory | ntf | 5 | 7.9 s |
| RateLimitTestFactory | ratelimit | 8 | 6.7 s |
| ChannelHookFactory | ntf | 5 | 6.6 s |
| PhoneVerificationEnabledFactory | api | 5 | 6.6 s |
| PushEnabledFactory | api | 8 | 6.1 s |
| StaffMaxTestFactory | api | 5 | 4.6 s |
| ProdLimitsHost | api | 5 | 4.5 s |
| TrialDispatchFactory | dispatch | 2 | 3.8 s |
| CompanyAddressTestFactory | addr | 3 | 3.5 s |
| PushDispatchTestFactory | dispatch | 2 | 2.2 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 2.1 s |
| CatalogTestFactory | api | 3 | 1.9 s |
| TightHost | api | 1 | 1.1 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 33.9 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 6.8 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 5.8 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 3.4 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 7.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 6.6 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 4.0 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 5.4 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 4.6 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 7.3 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 5.9 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 5.7 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 4.2 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 4.1 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 4.1 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.0 s |
| ServiceBooking.Tests.Tests.PhoneVerificationTests | 2 | 3.7 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:10.1 | 3:00.1 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 4:03.6 | 3:58.3 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:44.7 | 3:42.5 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:35.8 | 2:58.8 | 3 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:18.3 | 2:16.0 | 21 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:14.6 | 2:05.8 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:04.6 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 1:35.5 | 1:31.5 | 51 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:32.5 | 1:27.1 | 2 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:25.6 | 1:21.3 | 16 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:22.1 | 1:18.3 | 15 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:18.1 | 1:09.1 | 18 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:16.1 | 1:04.6 | 12 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:14.8 | 1:04.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:14.4 | 1:06.0 | 36 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 1:13.1 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:11.0 | 1:02.8 | 11 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:09.7 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:09.1 | 1:02.9 | 28 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:06.2 | 1:00.1 | 21 |
