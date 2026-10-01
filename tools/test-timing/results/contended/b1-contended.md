# Замер времени регресса: b1-instrumented

Коммит `7cbf866e5705`, 2026-10-01T07:48:20Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build, чужие прогоны: ДА (отчёт не годен для сравнения).

Итого медиана: **13:20.0**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| unit | 3 | 3.0 s | 2732 / 0 / 0 |
| functional | 2 | 12:56.7 | 1265 / 2 / 0 |
| vitest | 2 | 20.3 s | 1536 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 12:56.7.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.4 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.4 s | server-ready |
| Шаблон БД (миграции) | 3.8 s | template-ready |
| Создание баз классов | 28:19.1 суммарно, 124 шт. | class-db-created |
| Удаление баз классов | 1:59.4 суммарно | class-db-dropped |
| Старты хоста | 11:01.1 суммарно, 321 шт. | медиана 957 мс, p95 9177 мс |
| Тесты (wall набора) | 12:56.7 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.CompaniesTests (5:02.0), хвост 7:03.5.

CPU контейнера Postgres: макс 140 %, среднее 31 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 111 | 3:07.8 |
| NotificationTestFactory | ntf | 41 | 1:51.6 |
| PhoneVerificationEnabledFactory | api | 23 | 1:13.9 |
| PushDispatchTestFactory | dispatch | 22 | 1:09.6 |
| PushEnabledFactory | api | 17 | 44.7 s |
| RateLimitTestFactory | ratelimit | 8 | 36.7 s |
| CompanyAddressTestFactory | addr | 17 | 35.0 s |
| StaffMaxTestFactory | api | 22 | 34.1 s |
| ChannelHookFactory | ntf | 5 | 19.4 s |
| ProdLimitsHost | api | 5 | 14.6 s |
| LegalDocumentsTestFactory | legal | 13 | 12.5 s |
| CatalogTestFactory | api | 8 | 9.3 s |
| DemoHostFactory | demo | 18 | 4.3 s |
| NotificationDispatchTestFactory | dispatch | 6 | 3.2 s |
| TightHost | api | 1 | 2.1 s |
| TrialDispatchFactory | dispatch | 2 | 1.5 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.8 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 24 | 1:22.6 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 20 | 32.8 s |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 19 | 51.8 s |
| ServiceBooking.Tests.Tests.PhoneVerificationTests | 18 | 1:05.0 |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 18 | 37.2 s |
| (unknown) | 18 | 4.3 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 11 | 5.3 s |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 10 | 26.5 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 9 | 10.5 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 7 | 52.7 s |
| ServiceBooking.Tests.Tests.StaffPushSubscriptionAndQueueingTests | 7 | 32.5 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 7 | 12.8 s |
| ServiceBooking.Tests.Tests.Cycle25CatalogTests | 7 | 8.4 s |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 7 | 6.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 6 | 20.6 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 15.4 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 6 | 3.3 s |
| ServiceBooking.Tests.Tests.StaffPushDispatchTests | 5 | 21.0 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 9.3 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 4 | 2.6 s |

## Топ медленных классов: unit

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.UnitTests.DemoResetTests | 1.7 s | 1.7 s | 14 |
| ServiceBooking.UnitTests.ShowcaseOwnershipCoverageTests | 1.4 s | 1.4 s | 5 |
| ServiceBooking.UnitTests.ShowcaseDatasetTests | 1.3 s | 0.8 s | 17 |
| ServiceBooking.UnitTests.ShowcaseDemoProfileTests | 1.0 s | 0.8 s | 19 |
| ServiceBooking.UnitTests.OrderMoneyTests | 0.9 s | 0.9 s | 17 |
| ServiceBooking.UnitTests.ShowcaseAssetStoreTests | 0.7 s | 0.7 s | 13 |
| ServiceBooking.UnitTests.LegalKit.LegalPublishTests | 0.5 s | 0.4 s | 14 |
| ServiceBooking.UnitTests.ImageProcessorTests | 0.3 s | 0.2 s | 15 |
| ServiceBooking.UnitTests.AppLogAgeRuleTests | 0.2 s | 0.2 s | 4 |
| ServiceBooking.UnitTests.LegalKit.StrayArtifactFileTests | 0.2 s | 0.2 s | 9 |
| ServiceBooking.UnitTests.SubjectPhoneGateInvariantTests | 0.2 s | 0.0 s | 1 |
| ServiceBooking.UnitTests.LegalKit.LegalBuildDeterminismTests | 0.2 s | 0.1 s | 7 |
| ServiceBooking.UnitTests.DemoInstanceAndStorageTests | 0.2 s | 0.1 s | 23 |
| ServiceBooking.UnitTests.DeploymentSafetyChecksTests | 0.2 s | 0.2 s | 145 |
| ServiceBooking.UnitTests.LegalDocumentProviderTests | 0.1 s | 0.1 s | 31 |
| ServiceBooking.UnitTests.OrderDomainTests | 0.1 s | 0.1 s | 35 |
| ServiceBooking.UnitTests.LegalConsentFilterTests | 0.1 s | 0.0 s | 9 |
| ServiceBooking.UnitTests.QrImageTests | 0.1 s | 0.1 s | 5 |
| ServiceBooking.UnitTests.ShowcasePrimitivesTests | 0.1 s | 0.1 s | 18 |
| ServiceBooking.UnitTests.CompanyPhotoOrderingTests | 0.1 s | 0.0 s | 14 |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 5:14.8 | 3:21.7 | 14 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 5:11.6 | 4:55.4 | 18 |
| ServiceBooking.Tests.Tests.CompaniesTests | 5:09.9 | 5:02.0 | 85 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 5:00.5 | 4:57.7 | 63 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 4:54.2 | 4:48.8 | 21 |
| ServiceBooking.Tests.Tests.PaginationTests | 4:52.2 | 4:43.4 | 14 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 4:46.5 | 4:39.2 | 18 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 4:43.8 | 4:43.2 | 28 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 4:43.6 | 4:41.6 | 27 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 4:40.1 | 4:36.1 | 28 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 4:40.1 | 3:24.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 4:35.6 | 4:13.3 | 8 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 4:35.0 | 4:33.9 | 36 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 4:32.6 | 4:29.9 | 12 |
| ServiceBooking.Tests.Tests.MastersTests | 4:30.1 | 4:29.3 | 19 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 4:28.6 | 4:26.6 | 18 |
| ServiceBooking.Tests.Tests.AdminTests | 4:27.4 | 4:26.8 | 51 |
| ServiceBooking.Tests.Tests.ReviewsTests | 4:25.9 | 4:22.9 | 17 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 4:25.0 | 4:21.0 | 15 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 4:24.6 | 3:29.3 | 11 |

## vitest (203 файлов, pool threads)

Фазы (суммы по воркерам): transform 6.2 s, setup 11.9 s, collect 22.4 s, tests 41.9 s, environment 27.1 s, prepare 7.6 s.

| Файл | Время | Тесты | Накладные | Среда |
|---|---|---|---|---|
| goods/src/pages/cabinet/CreateShopPage.test.tsx | 3.1 s | 3.1 s | 0.0 s | jsdom |
| src/pages/owner/SettingsTab.qa32.test.tsx | 2.1 s | 2.1 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/LinkPage.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.test.tsx | 1.4 s | 1.4 s | 0.0 s | jsdom |
| goods/src/components/storefront/CartPanel.test.tsx | 1.1 s | 1.1 s | 0.0 s | jsdom |
| src/pages/owner/SalonProfileSection.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/pages/RegisterPage.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.showcase.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| goods/src/components/profile/ShopProfileSection.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/admin/BillingAccountsAdminTab.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/MasterClientsPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/SubjectRequestPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/components/company/CompanyPhotosSection.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/pages/owner/NotificationTemplatesTab.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/HoursPage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| goods/src/pages/StorefrontPage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/clientNotes/HealthNoteCard.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/pages/admin/SubjectRequestsTab.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| goods/src/components/catalog/ProductModal.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| goods/src/components/orders/IssueModal.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
