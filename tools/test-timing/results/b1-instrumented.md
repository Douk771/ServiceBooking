# Замер времени регресса: b1-instrumented

Коммит `7cbf866e5705`, 2026-10-01T10:04:53Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **7:29.5**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| unit | 3 | 2.7 s | 2732 / 0 / 0 |
| functional | 3 | 7:08.8 | 1266 / 1 / 0 |
| vitest | 3 | 17.9 s | 1536 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:08.8.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 1.5 s | server-ready |
| Шаблон БД (миграции) | 4.0 s | template-ready |
| Создание баз классов | 32:24.5 суммарно, 124 шт. | class-db-created |
| Удаление баз классов | 28.8 s суммарно | class-db-dropped |
| Старты хоста | 4:51.8 суммарно, 321 шт. | медиана 770 мс, p95 2246 мс |
| Тесты (wall набора) | 7:08.8 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28DemoScenarioTests (2:34.0), хвост 4:59.3.

CPU контейнера Postgres: макс 106 %, среднее 30 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

### Старты хоста по фабрикам

| Фабрика | Тег | Стартов | Сумма |
|---|---|---|---|
| CustomWebApplicationFactory | api | 111 | 2:05.3 |
| NotificationTestFactory | ntf | 41 | 36.0 s |
| PhoneVerificationEnabledFactory | api | 23 | 20.9 s |
| StaffMaxTestFactory | api | 22 | 19.1 s |
| PushEnabledFactory | api | 17 | 17.1 s |
| CompanyAddressTestFactory | addr | 17 | 15.2 s |
| PushDispatchTestFactory | dispatch | 22 | 11.6 s |
| CatalogTestFactory | api | 8 | 9.3 s |
| LegalDocumentsTestFactory | legal | 13 | 8.3 s |
| RateLimitTestFactory | ratelimit | 8 | 7.1 s |
| ProdLimitsHost | api | 5 | 5.3 s |
| NotificationDispatchTestFactory | dispatch | 6 | 5.0 s |
| UploadsStaticFilesTestFactory | uploads | 2 | 3.8 s |
| DemoHostFactory | demo | 18 | 3.3 s |
| ChannelHookFactory | ntf | 5 | 3.0 s |
| TrialDispatchFactory | dispatch | 2 | 1.0 s |
| TightHost | api | 1 | 0.4 s |

### Классы по числу стартов хоста

| Класс | Стартов | Сумма |
|---|---|---|
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 24 | 19.2 s |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 20 | 18.5 s |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 19 | 10.0 s |
| ServiceBooking.Tests.Tests.CompanyAddressTests | 18 | 16.3 s |
| ServiceBooking.Tests.Tests.PhoneVerificationTests | 18 | 14.8 s |
| (unknown) | 18 | 3.3 s |
| ServiceBooking.Tests.Tests.NotificationWebhookUnsubscribeTests | 11 | 9.0 s |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 10 | 12.6 s |
| ServiceBooking.Tests.Tests.LegalConsentVersionChangeTests | 9 | 6.0 s |
| ServiceBooking.Tests.Tests.StaffPushSubscriptionAndQueueingTests | 7 | 11.4 s |
| ServiceBooking.Tests.Tests.Cycle25CatalogTests | 7 | 8.9 s |
| ServiceBooking.Tests.Tests.Cycle23StrictModeAndDataTests | 7 | 7.7 s |
| ServiceBooking.Tests.Tests.RateLimitingTests | 7 | 6.5 s |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 7 | 5.1 s |
| ServiceBooking.Tests.Tests.Cycle31GalleryRateLimitTests | 6 | 6.7 s |
| ServiceBooking.Tests.Tests.LegalPricingGateTests | 6 | 4.8 s |
| ServiceBooking.Tests.Tests.Cycle18TrialMailingWindowHookTests | 6 | 3.5 s |
| ServiceBooking.Tests.Tests.StaffPushDispatchTests | 5 | 3.4 s |
| ServiceBooking.Tests.Tests.LegalPriorityTests | 4 | 4.3 s |
| ServiceBooking.Tests.Tests.Cycle25WorkingDayTests | 4 | 4.1 s |

## Топ медленных классов: unit

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.UnitTests.DemoResetTests | 1.1 s | 1.1 s | 14 |
| ServiceBooking.UnitTests.ShowcaseOwnershipCoverageTests | 0.9 s | 0.9 s | 5 |
| ServiceBooking.UnitTests.ShowcaseDemoProfileTests | 0.8 s | 0.6 s | 19 |
| ServiceBooking.UnitTests.OrderMoneyTests | 0.8 s | 0.7 s | 17 |
| ServiceBooking.UnitTests.ShowcaseDatasetTests | 0.7 s | 0.4 s | 17 |
| ServiceBooking.UnitTests.ShowcaseAssetStoreTests | 0.5 s | 0.5 s | 13 |
| ServiceBooking.UnitTests.AppLogAgeRuleTests | 0.4 s | 0.5 s | 4 |
| ServiceBooking.UnitTests.LegalKit.LegalPublishTests | 0.4 s | 0.3 s | 14 |
| ServiceBooking.UnitTests.ImageProcessorTests | 0.3 s | 0.2 s | 15 |
| ServiceBooking.UnitTests.LegalDocumentProviderTests | 0.2 s | 0.2 s | 31 |
| ServiceBooking.UnitTests.LegalKit.LegalBuildDeterminismTests | 0.2 s | 0.1 s | 7 |
| ServiceBooking.UnitTests.LegalKit.StrayArtifactFileTests | 0.2 s | 0.1 s | 9 |
| ServiceBooking.UnitTests.OrderDomainTests | 0.1 s | 0.1 s | 35 |
| ServiceBooking.UnitTests.AdminLegalControllerReadinessHelpersTests | 0.1 s | 0.1 s | 26 |
| ServiceBooking.UnitTests.TemplateAdHeuristicsTests | 0.1 s | 0.1 s | 7 |
| ServiceBooking.UnitTests.DeploymentSafetyChecksTests | 0.1 s | 0.1 s | 145 |
| ServiceBooking.UnitTests.LegalConsentFilterTests | 0.1 s | 0.1 s | 9 |
| ServiceBooking.UnitTests.DemoMaintenanceTests | 0.1 s | 0.1 s | 47 |
| ServiceBooking.UnitTests.SubjectPhoneGateInvariantTests | 0.1 s | 0.0 s | 1 |
| ServiceBooking.UnitTests.ShowcasePrimitivesTests | 0.1 s | 0.1 s | 18 |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 2:07.2 | 2:03.1 | 63 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 1:58.2 | 1:50.8 | 18 |
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 1:57.2 | 1:51.4 | 14 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:54.1 | 1:46.9 | 19 |
| ServiceBooking.Tests.Tests.Cycle23OrdersTests | 1:45.6 | 1:38.8 | 18 |
| ServiceBooking.Tests.Tests.AdminTests | 1:41.4 | 1:40.8 | 51 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:41.0 | 1:38.7 | 28 |
| ServiceBooking.Tests.Tests.MastersTests | 1:40.4 | 1:32.6 | 19 |
| ServiceBooking.Tests.Tests.Cycle24PickupTests | 1:37.2 | 1:34.5 | 16 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 1:36.3 | 1:31.0 | 18 |
| ServiceBooking.Tests.Tests.ReviewsTests | 1:35.0 | 1:27.0 | 17 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 1:34.2 | 1:23.1 | 21 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 1:33.7 | 1:29.3 | 11 |
| ServiceBooking.Tests.Tests.Cycle25PickListTests | 1:31.3 | 1:21.7 | 5 |
| ServiceBooking.Tests.Tests.Cycle22ChannelFundingTests | 1:30.8 | 1:07.2 | 9 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 1:30.5 | 1:23.0 | 36 |
| ServiceBooking.Tests.Tests.Cycle24TariffTests | 1:27.8 | 1:25.3 | 12 |
| ServiceBooking.Tests.Tests.CompaniesTests | 1:27.6 | 1:22.1 | 85 |
| ServiceBooking.Tests.Tests.PaginationTests | 1:27.5 | 58.6 s | 14 |
| ServiceBooking.Tests.Tests.CompanyPhotosTests | 1:25.4 | 1:22.9 | 21 |

## vitest (203 файлов, pool threads)

Фазы (суммы по воркерам): transform 6.0 s, setup 10.7 s, collect 19.6 s, tests 36.8 s, environment 24.4 s, prepare 7.3 s.

| Файл | Время | Тесты | Накладные | Среда |
|---|---|---|---|---|
| goods/src/pages/cabinet/CreateShopPage.test.tsx | 3.0 s | 3.0 s | 0.0 s | jsdom |
| src/pages/owner/SettingsTab.qa32.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/LinkPage.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.test.tsx | 1.4 s | 1.4 s | 0.0 s | jsdom |
| goods/src/components/storefront/CartPanel.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.showcase.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/pages/RegisterPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/owner/SalonProfileSection.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/admin/BillingAccountsAdminTab.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/SubjectRequestPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| goods/src/components/profile/ShopProfileSection.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/pages/MasterClientsPage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/StorefrontPage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/pages/owner/NotificationTemplatesTab.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/company/CompanyPhotosSection.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/clientNotes/HealthNoteCard.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| goods/src/components/orders/IssueModal.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/HoursPage.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/CatalogPage.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
| src/pages/CabinetPage.test.tsx | 0.5 s | 0.5 s | 0.0 s | jsdom |
