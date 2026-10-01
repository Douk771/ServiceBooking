# Замер времени регресса: b0-baseline

Коммит `a1e225942098`, 2026-10-01T09:41:56Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build.

Итого медиана: **7:33.2**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| unit | 3 | 3.0 s | 2732 / 0 / 0 |
| functional | 3 | 7:04.3 | 1266 / 1 / 0 |
| vitest | 3 | 26.0 s | 1541 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 7:04.3.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 1.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 0.0 s | server-ready |
| Шаблон БД (миграции) | 0.0 s | template-ready |
| Создание баз классов | 0.0 s суммарно, 0 шт. | class-db-created |
| Удаление баз классов | 0.0 s суммарно | class-db-dropped |
| Старты хоста | 0.0 s суммарно, 0 шт. | медиана 0 мс, p95 0 мс |
| Тесты (wall набора) | 7:04.3 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28DemoScenarioTests (2:14.5), хвост 4:51.7.

CPU контейнера Postgres: макс 100 %, среднее 31 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

## Топ медленных классов: unit

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.UnitTests.DemoResetTests | 1.2 s | 1.2 s | 14 |
| ServiceBooking.UnitTests.ShowcaseOwnershipCoverageTests | 1.2 s | 1.2 s | 5 |
| ServiceBooking.UnitTests.ShowcaseDatasetTests | 1.1 s | 0.7 s | 17 |
| ServiceBooking.UnitTests.ShowcaseDemoProfileTests | 1.1 s | 0.8 s | 19 |
| ServiceBooking.UnitTests.OrderMoneyTests | 0.7 s | 0.7 s | 17 |
| ServiceBooking.UnitTests.AppLogAgeRuleTests | 0.7 s | 0.7 s | 4 |
| ServiceBooking.UnitTests.LegalKit.LegalPublishTests | 0.4 s | 0.4 s | 14 |
| ServiceBooking.UnitTests.ShowcaseAssetStoreTests | 0.2 s | 0.2 s | 13 |
| ServiceBooking.UnitTests.ImageProcessorTests | 0.2 s | 0.2 s | 15 |
| ServiceBooking.UnitTests.LegalKit.StrayArtifactFileTests | 0.2 s | 0.2 s | 9 |
| ServiceBooking.UnitTests.LegalDocumentProviderTests | 0.2 s | 0.2 s | 31 |
| ServiceBooking.UnitTests.OrderDomainTests | 0.2 s | 0.2 s | 35 |
| ServiceBooking.UnitTests.LegalKit.LegalBuildDeterminismTests | 0.2 s | 0.2 s | 7 |
| ServiceBooking.UnitTests.SubjectPhoneGateInvariantTests | 0.1 s | 0.0 s | 1 |
| ServiceBooking.UnitTests.AdminLegalControllerReadinessHelpersTests | 0.1 s | 0.1 s | 26 |
| ServiceBooking.UnitTests.DeploymentSafetyChecksTests | 0.1 s | 0.1 s | 145 |
| ServiceBooking.UnitTests.LegalConsentFilterTests | 0.1 s | 0.1 s | 9 |
| ServiceBooking.UnitTests.ShowcasePrimitivesTests | 0.1 s | 0.1 s | 18 |
| ServiceBooking.UnitTests.DemoInstanceAndStorageTests | 0.1 s | 0.1 s | 23 |
| ServiceBooking.UnitTests.LegalControllerGetDocumentTests | 0.1 s | 0.1 s | 8 |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.CompaniesTests | 2:02.7 | 1:59.6 | 85 |
| ServiceBooking.Tests.Tests.PaginationTests | 1:58.1 | 1:57.4 | 14 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 1:52.4 | 1:47.6 | 28 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 1:48.3 | 1:37.8 | 63 |
| ServiceBooking.Tests.Tests.MastersTests | 1:46.9 | 1:43.7 | 19 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 1:46.3 | 1:42.3 | 28 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 1:45.3 | 1:36.0 | 21 |
| ServiceBooking.Tests.Tests.AdminTests | 1:44.2 | 1:43.1 | 51 |
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 1:42.6 | 1:40.9 | 22 |
| ServiceBooking.Tests.Tests.ManualBookingFreedomTests | 1:42.3 | 1:37.2 | 18 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 1:41.3 | 1:20.4 | 8 |
| ServiceBooking.Tests.Tests.Cycle23StaffOrdersTests | 1:40.2 | 1:31.2 | 16 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 1:38.1 | 1:26.4 | 11 |
| ServiceBooking.Tests.Tests.BookingHistoryTests | 1:36.3 | 1:31.1 | 16 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 1:34.8 | 1:27.5 | 15 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 1:34.1 | 1:18.3 | 19 |
| ServiceBooking.Tests.Tests.Cycle24NotificationsTests | 1:33.1 | 1:22.8 | 12 |
| ServiceBooking.Tests.Tests.Cycle24TariffTests | 1:28.6 | 1:19.4 | 12 |
| ServiceBooking.Tests.Tests.Cycle25ReportsTests | 1:28.2 | 46.0 s | 11 |
| ServiceBooking.Tests.Tests.ReviewsTests | 1:28.0 | 1:19.3 | 17 |

## vitest (203 файлов, pool forks)

Фазы (суммы по воркерам): transform 7.2 s, setup 16.4 s, collect 26.0 s, tests 41.7 s, environment 52.1 s, prepare 8.5 s.

| Файл | Время | Тесты | Накладные | Среда |
|---|---|---|---|---|
| goods/src/pages/cabinet/CreateShopPage.test.tsx | 3.0 s | 3.0 s | 0.0 s | jsdom |
| src/pages/owner/SettingsTab.qa32.test.tsx | 1.9 s | 1.9 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/LinkPage.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.test.tsx | 1.4 s | 1.4 s | 0.0 s | jsdom |
| goods/src/components/storefront/CartPanel.test.tsx | 1.1 s | 1.1 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.showcase.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/pages/admin/BillingAccountsAdminTab.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/owner/SalonProfileSection.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| goods/src/components/profile/ShopProfileSection.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/RegisterPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/MasterClientsPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/SubjectRequestPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/components/company/CompanyPhotosSection.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/StorefrontPage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/HoursPage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/pages/owner/NotificationTemplatesTab.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/components/clientNotes/HealthNoteCard.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.captcha.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| src/pages/owner/CompanyManagePage.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
| goods/src/components/orders/IssueModal.test.tsx | 0.6 s | 0.6 s | 0.0 s | jsdom |
