# Замер времени регресса: b1-quiet

Коммит `7cbf866e5705`, 2026-10-01T09:21:20Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build, чужие прогоны: ДА (отчёт не годен для сравнения).

Итого медиана: **12:45.1**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| functional | 1 | 12:45.1 | 1266 / 1 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 12:45.1.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.7 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.8 s | server-ready |
| Шаблон БД (миграции) | 4.7 s | template-ready |
| Создание баз классов | 53:45.9 суммарно, 124 шт. | class-db-created |
| Удаление баз классов | 1:10.5 суммарно | class-db-dropped |
| Старты хоста | 8:53.1 суммарно, 321 шт. | медиана 1137 мс, p95 5648 мс |
| Тесты (wall набора) | 12:45.1 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28DemoScenarioTests (6:08.6), хвост 8:45.6.

CPU контейнера Postgres: макс 138 %, среднее 31 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 111 | 3:57.0 |
| NotificationTestFactory | ntf | 41 | 1:01.8 |
| PhoneVerificationEnabledFactory | api | 23 | 40.3 s |
| StaffMaxTestFactory | api | 22 | 35.9 s |
| PushDispatchTestFactory | dispatch | 22 | 28.7 s |
| CompanyAddressTestFactory | addr | 17 | 27.9 s |
| PushEnabledFactory | api | 17 | 19.8 s |
| CatalogTestFactory | api | 8 | 15.4 s |
| LegalDocumentsTestFactory | legal | 13 | 13.8 s |
| NotificationDispatchTestFactory | dispatch | 6 | 10.4 s |
| TrialDispatchFactory | dispatch | 2 | 9.7 s |
| ProdLimitsHost | api | 5 | 9.1 s |
| DemoHostFactory | demo | 18 | 8.2 s |
| RateLimitTestFactory | ratelimit | 8 | 7.8 s |
| ChannelHookFactory | ntf | 5 | 5.9 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 1.1 s |
| TightHost | api | 1 | 0.3 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 24 | 37.9 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 20 | 32.9 s |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 19 | 22.2 s |
| ServiceBooking.Tests.Tests.PhoneVerificationTests | 18 | 37.1 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 18 | 29.2 s |
| (unknown) | 18 | 8.2 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 11 | 9.2 s |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 10 | 27.3 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 9 | 11.7 s |
| ServiceBooking.Tests.Tests.Cycle25CatalogTests | 7 | 13.6 s |
| ServiceBooking.Tests.Tests.StaffPushSubscriptionAndQueueingTests | 7 | 12.3 s |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 7 | 7.2 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 7 | 6.7 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 7 | 5.1 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 11.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 6 | 9.8 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 6 | 5.4 s |
| ServiceBooking.Tests.Tests.StaffPushDispatchTests | 5 | 13.0 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 4 | 6.7 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 3.9 s |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 3:51.4 | 3:46.5 | 63 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3:34.4 | 3:27.6 | 19 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 3:27.4 | 3:18.5 | 21 |
| ServiceBooking.Tests.Tests.PaginationTests | 3:22.1 | 3:12.2 | 14 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 3:17.3 | 3:08.2 | 18 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 3:15.0 | 3:06.6 | 28 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 3:09.7 | 3:05.2 | 27 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 3:09.6 | 2:58.6 | 12 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3:04.3 | 2:46.8 | 11 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 3:03.4 | 2:49.4 | 14 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 3:01.8 | 3:00.8 | 28 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 3:00.1 | 2:59.2 | 36 |
| ServiceBooking.Tests.Tests.CompaniesTests | 2:56.5 | 2:49.3 | 85 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 2:55.0 | 2:46.7 | 18 |
| ServiceBooking.Tests.Tests.MastersTests | 2:53.8 | 2:47.1 | 19 |
| ServiceBooking.Tests.Tests.AdminTests | 2:53.7 | 2:49.3 | 51 |
| ServiceBooking.Tests.Tests.ReviewsTests | 2:51.3 | 2:52.2 | 17 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 2:46.6 | 2:12.7 | 11 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 2:46.1 | 2:11.5 | 18 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 2:41.3 | 2:36.3 | 16 |
