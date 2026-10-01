# Замер времени регресса: final

Коммит `6723a9fd5a89`, 2026-10-01T13:55:16Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **3:30.9**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| unit | 3 | 2.7 s | 2732 / 0 / 0 |
| functional | 3 | 3:10.3 | 1267 / 0 / 0 |
| vitest | 3 | 17.9 s | 1536 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 3:10.3.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 18.1 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 3.8 s | template-ready |
| Создание баз классов | 29.8 s суммарно, 117 шт. | class-db-created |
| Удаление баз классов | 17.6 s суммарно | class-db-dropped |
| Старты хоста | 2:14.6 суммарно, 181 шт. | медиана 676 мс, p95 1511 мс |
| Тесты (wall набора) | 3:10.3 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests (2:04.4), хвост 2.9 s.

CPU контейнера Postgres: макс 131 %, среднее 53 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 99 | 1:26.4 |
| LegalDocumentsTestFactory | legal | 13 | 5.7 s |
| NotificationTestFactory | ntf | 5 | 4.8 s |
| RateLimitTestFactory | ratelimit | 8 | 4.5 s |
| DemoHostFactory | demo | 11 | 4.3 s |
| StaffMaxTestFactory | api | 5 | 4.1 s |
| NotificationDispatchTestFactory | dispatch | 6 | 4.1 s |
| ProdLimitsHost | api | 5 | 3.9 s |
| ChannelHookFactory | ntf | 5 | 3.6 s |
| PhoneVerificationEnabledFactory | api | 5 | 2.6 s |
| PushDispatchTestFactory | dispatch | 2 | 2.4 s |
| PushEnabledFactory | api | 6 | 2.2 s |
| CompanyAddressTestFactory | addr | 3 | 1.8 s |
| CatalogTestFactory | api | 3 | 1.7 s |
| TrialDispatchFactory | dispatch | 2 | 1.2 s |
| TightHost | api | 1 | 0.7 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 0.6 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| (unknown) | 11 | 4.3 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 8 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 4.3 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 6 | 3.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 5 | 3.6 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 5 | 3.5 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 5 | 1.4 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 2.8 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 2.3 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 4 | 2.2 s |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 3 | 3.7 s |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 3 | 2.7 s |
| ServiceBooking.Tests.Tests.Cycle18TrialPhoneRetentionTests | 3 | 2.3 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 3 | 2.2 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingDeliveryTests | 3 | 2.2 s |
| ServiceBooking.Tests.Tests.GuestDataGateCycle16Tests | 3 | 1.9 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 3 | 1.8 s |
| ServiceBooking.Tests.Tests.NotificationDispatchExtraTests | 3 | 0.9 s |
| ServiceBooking.Tests.Tests.Cycle29QaTests | 2 | 2.4 s |
| ServiceBooking.Tests.Tests.Cycle29ContractTests | 2 | 2.4 s |

## Топ медленных классов: unit

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.UnitTests.DemoResetTests | 1.4 s | 1.4 s | 14 |
| ServiceBooking.UnitTests.ShowcaseDatasetTests | 1.1 s | 0.7 s | 17 |
| ServiceBooking.UnitTests.OrderMoneyTests | 1.1 s | 1.1 s | 17 |
| ServiceBooking.UnitTests.ShowcaseDemoProfileTests | 1.0 s | 0.8 s | 19 |
| ServiceBooking.UnitTests.ShowcaseAssetStoreTests | 0.9 s | 0.8 s | 13 |
| ServiceBooking.UnitTests.ShowcaseOwnershipCoverageTests | 0.6 s | 0.6 s | 5 |
| ServiceBooking.UnitTests.LegalKit.LegalPublishTests | 0.4 s | 0.3 s | 14 |
| ServiceBooking.UnitTests.ImageProcessorTests | 0.3 s | 0.2 s | 15 |
| ServiceBooking.UnitTests.AppLogAgeRuleTests | 0.3 s | 0.3 s | 4 |
| ServiceBooking.UnitTests.LegalDocumentProviderTests | 0.2 s | 0.2 s | 31 |
| ServiceBooking.UnitTests.LegalKit.StrayArtifactFileTests | 0.2 s | 0.2 s | 9 |
| ServiceBooking.UnitTests.OrderDomainTests | 0.2 s | 0.2 s | 35 |
| ServiceBooking.UnitTests.LegalKit.LegalBuildDeterminismTests | 0.1 s | 0.1 s | 7 |
| ServiceBooking.UnitTests.DemoInstanceAndStorageTests | 0.1 s | 0.1 s | 23 |
| ServiceBooking.UnitTests.SubjectPhoneGateInvariantTests | 0.1 s | 0.0 s | 1 |
| ServiceBooking.UnitTests.LegalControllerGetDocumentTests | 0.1 s | 0.1 s | 8 |
| ServiceBooking.UnitTests.DeploymentSafetyChecksTests | 0.1 s | 0.1 s | 145 |
| ServiceBooking.UnitTests.ShowcasePrimitivesTests | 0.1 s | 0.1 s | 18 |
| ServiceBooking.UnitTests.DemoMaintenanceTests | 0.1 s | 0.1 s | 47 |
| ServiceBooking.UnitTests.LegalConsentFilterTests | 0.1 s | 0.1 s | 9 |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle28ShowcaseLifecycleTests | 2:23.4 | 2:04.4 | 7 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 2:00.5 | 1:58.8 | 63 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:43.6 | 1:43.2 | 85 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:17.4 | 1:13.5 | 14 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseFreshDeleteTests | 1:02.6 | 0.0 s | 1 |
| ServiceBooking.Tests.Tests.Cycle28DemoMutationTests | 55.6 s | 1:50.4 | 4 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 54.2 s | 48.1 s | 18 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 51.7 s | 45.1 s | 19 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 47.8 s | 45.2 s | 11 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 45.5 s | 42.7 s | 21 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 44.3 s | 37.8 s | 28 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 39.5 s | 37.4 s | 27 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 39.4 s | 36.3 s | 16 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 39.0 s | 32.7 s | 11 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 38.8 s | 32.9 s | 18 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 38.4 s | 31.9 s | 8 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 37.2 s | 33.2 s | 15 |
| ServiceBooking.Tests.Tests.AdminTests | 37.1 s | 35.9 s | 51 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 35.3 s | 33.4 s | 28 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 35.0 s | 33.1 s | 36 |

## vitest (203 файлов, pool threads)

Фазы (суммы по воркерам): transform 6.0 s, setup 10.0 s, collect 20.0 s, tests 37.4 s, environment 24.5 s, prepare 6.9 s.

| Файл | Время | Тесты | Накладные | Среда |
|---|---|---|---|---|
| goods/src/pages/cabinet/CreateShopPage.test.tsx | 3.1 s | 3.1 s | 0.0 s | jsdom |
| src/pages/owner/SettingsTab.qa32.test.tsx | 1.9 s | 1.9 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/LinkPage.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.test.tsx | 1.4 s | 1.4 s | 0.0 s | jsdom |
| goods/src/components/storefront/CartPanel.test.tsx | 1.1 s | 1.1 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.showcase.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/pages/RegisterPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/admin/BillingAccountsAdminTab.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/owner/SalonProfileSection.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/MasterClientsPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/SubjectRequestPage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/components/profile/ShopProfileSection.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/pages/owner/NotificationTemplatesTab.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/StorefrontPage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/company/CompanyPhotosSection.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/pages/owner/CompanyManagePage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/clientNotes/HealthNoteCard.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| src/pages/admin/NoticesAdminTab.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.captcha.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| goods/src/components/orders/IssueModal.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
