# Замер времени регресса: b1-routes

Коммит `7cbf866e5705`, 2026-10-01T09:34:10Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Примечание: route-log enabled: timings not comparable.

Итого медиана: **7:19.0**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 7:19.0 | 1265 / 2 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:19.0.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.8 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 3.8 s | template-ready |
| Создание баз классов | 25:20.0 суммарно, 124 шт. | class-db-created |
| Удаление баз классов | 27.3 s суммарно | class-db-dropped |
| Старты хоста | 4:16.1 суммарно, 321 шт. | медиана 632 мс, p95 1879 мс |
| Тесты (wall набора) | 7:19.0 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28DemoScenarioTests (2:42.0), хвост 5:19.7.

CPU контейнера Postgres: макс 99 %, среднее 30 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 111 | 1:47.6 |
| NotificationTestFactory | ntf | 41 | 29.4 s |
| PhoneVerificationEnabledFactory | api | 23 | 21.1 s |
| CompanyAddressTestFactory | addr | 17 | 17.6 s |
| StaffMaxTestFactory | api | 22 | 15.8 s |
| PushDispatchTestFactory | dispatch | 22 | 15.7 s |
| PushEnabledFactory | api | 17 | 8.9 s |
| CatalogTestFactory | api | 8 | 7.1 s |
| LegalDocumentsTestFactory | legal | 13 | 7.0 s |
| NotificationDispatchTestFactory | dispatch | 6 | 6.2 s |
| RateLimitTestFactory | ratelimit | 8 | 4.9 s |
| DemoHostFactory | demo | 18 | 4.4 s |
| ChannelHookFactory | ntf | 5 | 3.7 s |
| ProdLimitsHost | api | 5 | 3.0 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 1.6 s |
| TrialDispatchFactory | dispatch | 2 | 1.6 s |
| TightHost | api | 1 | 0.4 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 24 | 21.7 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 20 | 13.9 s |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 19 | 14.6 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 18 | 19.1 s |
| ServiceBooking.Tests.Tests.PhoneVerificationTests | 18 | 16.0 s |
| (unknown) | 18 | 4.4 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 11 | 5.4 s |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 10 | 6.0 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 9 | 4.7 s |
| ServiceBooking.Tests.Tests.Cycle25CatalogTests | 7 | 6.9 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 7 | 6.1 s |
| ServiceBooking.Tests.Tests.StaffPushSubscriptionAndQueueingTests | 7 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 7 | 4.1 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 7 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 6 | 4.9 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.8 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 6 | 3.5 s |
| ServiceBooking.Tests.Tests.StaffPushDispatchTests | 5 | 2.5 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 4.3 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 4 | 3.7 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:53.5 | 1:50.6 | 14 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:48.4 | 1:42.9 | 85 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:46.9 | 1:39.3 | 18 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:42.5 | 1:39.1 | 11 |
| ServiceBooking.Tests.Tests.PaginationTests | 1:42.0 | 1:36.5 | 14 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:40.8 | 1:31.7 | 19 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:39.5 | 1:37.5 | 28 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 1:36.4 | 1:29.3 | 21 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:35.4 | 1:29.8 | 18 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 1:29.5 | 1:18.9 | 11 |
| ServiceBooking.Tests.Tests.AdminTests | 1:28.4 | 1:28.1 | 51 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 1:27.3 | 1:18.7 | 8 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:26.6 | 1:20.5 | 15 |
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 1:26.6 | 1:20.7 | 22 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 1:26.2 | 1:22.8 | 18 |
| ServiceBooking.Tests.Tests.ReviewsTests | 1:25.8 | 1:25.0 | 17 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:25.0 | 1:21.9 | 28 |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 1:23.5 | 1:20.7 | 9 |
| ServiceBooking.Tests.Tests.Cycle25PickListTests | 1:19.7 | 1:04.4 | 5 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 1:19.5 | 1:01.2 | 7 |
