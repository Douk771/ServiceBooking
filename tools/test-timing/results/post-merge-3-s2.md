# Замер времени регресса: post-merge-3-s2

Коммит `18c3dfd2f9ae`, 2026-10-01T16:55:42Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **5:41.0**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 5:41.0 | 1299 / 1 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 5:41.0.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.2 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.2 s | template-ready |
| Создание баз классов | 1:17.2 суммарно, 141 шт. | class-db-created |
| Удаление баз классов | 31.1 s суммарно | class-db-dropped |
| Старты хоста | 4:14.2 суммарно, 202 шт. | медиана 1213 мс, p95 2741 мс |
| Тесты (wall набора) | 5:41.0 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.BookingsFlowSmokeTests (4:05.1), хвост 2.6 s.

CPU контейнера Postgres: макс 157 %, среднее 51 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 100 | 2:25.9 |
| DemoHostFactory | demo | 29 | 36.9 s |
| LegalDocumentsTestFactory | legal | 13 | 9.9 s |
| PushEnabledFactory | api | 8 | 7.9 s |
| ChannelHookFactory | ntf | 5 | 7.8 s |
| NotificationTestFactory | ntf | 5 | 6.7 s |
| NotificationDispatchTestFactory | dispatch | 6 | 6.6 s |
| CompanyAddressTestFactory | addr | 3 | 6.5 s |
| RateLimitTestFactory | ratelimit | 8 | 6.3 s |
| StaffMaxTestFactory | api | 5 | 4.2 s |
| PhoneVerificationEnabledFactory | api | 5 | 4.1 s |
| TrialDispatchFactory | dispatch | 2 | 4.0 s |
| ProdLimitsHost | api | 5 | 3.5 s |
| PushDispatchTestFactory | dispatch | 2 | 1.9 s |
| CatalogTestFactory | api | 3 | 1.4 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.5 s |
| TightHost | api | 1 | 0.2 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 29 | 36.9 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.7 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 7.8 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 6.4 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 5.2 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 7.3 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 4.0 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.0 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 7.4 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 6.3 s |
| ServiceBooking.Tests.Tests.Cycle35OffDemoTests | 3 | 3.9 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.8 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 3.5 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 3.1 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 2.9 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.8 s |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 2 | 4.6 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 4:20.9 | 3:07.7 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 4:10.4 | 4:05.1 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:44.1 | 3:42.1 | 85 |
| ServiceBooking.Tests.Tests.Cycle35DemoResetTests | 2:30.7 | 2:46.8 | 3 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 2:27.9 | 2:26.2 | 21 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 2:19.3 | 2:07.1 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 2:08.2 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.AdminTests | 1:49.8 | 1:45.4 | 51 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:37.8 | 1:32.6 | 16 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 1:30.5 | 1:17.9 | 12 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:29.6 | 1:25.9 | 15 |
| ServiceBooking.Tests.Tests.Cycle28DemoResetTests | 1:21.1 | 1:20.7 | 2 |
| ServiceBooking.Tests.Tests.Cycle28DemoContractTests | 1:15.9 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:14.3 | 1:05.2 | 36 |
| ServiceBooking.Tests.Tests.Cycle35DemoContractTests | 1:13.4 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseContentTests | 1:13.3 | 29.0 s | 14 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:13.0 | 1:02.1 | 19 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:12.7 | 1:01.8 | 21 |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 1:11.5 | 1:06.9 | 18 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 1:09.9 | 1:13.1 | 3 |
