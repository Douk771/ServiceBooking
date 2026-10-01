# Замер времени регресса: b0-baseline

Коммит `a1e225942098`, 2026-10-01T06:46:45Z. Хост MacBook-Pro-Ila.local (macOS 26.6.2 (arm64)), 8 ядер, 8.0 ГБ, Docker: colima, режим БД: container, P=4, сборка: no-build, чужие прогоны: ДА (отчёт не годен для сравнения).

Итого медиана: **11:24.0**

## Наборы

| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |
|---|---|---|---|
| unit | 3 | 4.3 s | 2732 / 0 / 0 |
| functional | 3 | 10:47.6 | 1266 / 1 / 0 |
| vitest | 3 | 32.1 s | 1541 / 0 / 0 |

## Функциональный набор: разбивка по фазам

Суммы по всем параллельным потокам — это не доли wall; wall набора 10:47.6.

| Фаза | Время | Примечание |
|---|---|---|
| Сборка (`dotnet build`, вне замера) | 2.9 s | прогоны с --no-build |
| Старт контейнера / подключение к серверу | 0.0 s | server-ready |
| Шаблон БД (миграции) | 0.0 s | template-ready |
| Создание баз классов | 0.0 s суммарно, 0 шт. | class-db-created |
| Удаление баз классов | 0.0 s суммарно | class-db-dropped |
| Старты хоста | 0.0 s суммарно, 0 шт. | медиана 0 мс, p95 0 мс |
| Тесты (wall набора) | 10:47.6 | |

Планирование: простой потоков 0.0 %, самый длинный класс ServiceBooking.Tests.Tests.Cycle28DemoScenarioTests (4:02.3), хвост 7:02.6.

CPU контейнера Postgres: макс 124 %, среднее 32 %, лимит 2 ядер.

EF `ManyServiceProvidersCreatedWarning` в выводе: нет.

## Топ медленных классов: unit

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.UnitTests.ShowcaseOwnershipCoverageTests | 1.9 s | 1.9 s | 5 |
| ServiceBooking.UnitTests.ShowcaseDatasetTests | 1.9 s | 1.1 s | 17 |
| ServiceBooking.UnitTests.DemoResetTests | 1.8 s | 1.8 s | 14 |
| ServiceBooking.UnitTests.ShowcaseDemoProfileTests | 1.6 s | 1.2 s | 19 |
| ServiceBooking.UnitTests.OrderMoneyTests | 1.0 s | 1.0 s | 17 |
| ServiceBooking.UnitTests.ImageProcessorTests | 0.8 s | 0.5 s | 15 |
| ServiceBooking.UnitTests.LegalKit.LegalPublishTests | 0.8 s | 0.7 s | 14 |
| ServiceBooking.UnitTests.LegalKit.StrayArtifactFileTests | 0.5 s | 0.3 s | 9 |
| ServiceBooking.UnitTests.SubjectPhoneGateInvariantTests | 0.4 s | 0.0 s | 1 |
| ServiceBooking.UnitTests.LegalDocumentProviderTests | 0.4 s | 0.3 s | 31 |
| ServiceBooking.UnitTests.OrderDomainTests | 0.4 s | 0.4 s | 35 |
| ServiceBooking.UnitTests.LegalKit.LegalBuildDeterminismTests | 0.3 s | 0.2 s | 7 |
| ServiceBooking.UnitTests.AdminLegalControllerReadinessHelpersTests | 0.3 s | 0.3 s | 26 |
| ServiceBooking.UnitTests.LegalControllerGetDocumentTests | 0.2 s | 0.1 s | 8 |
| ServiceBooking.UnitTests.AppLogAgeRuleTests | 0.2 s | 0.2 s | 4 |
| ServiceBooking.UnitTests.DeploymentSafetyChecksTests | 0.2 s | 0.2 s | 145 |
| ServiceBooking.UnitTests.VapidKeyValidatorTests | 0.2 s | 0.2 s | 10 |
| ServiceBooking.UnitTests.ShowcasePrimitivesTests | 0.1 s | 0.1 s | 18 |
| ServiceBooking.UnitTests.ShowcaseAssetStoreTests | 0.1 s | 0.1 s | 13 |
| ServiceBooking.UnitTests.DemoMaintenanceTests | 0.1 s | 0.1 s | 47 |

## Топ медленных классов: functional

| Класс | Сумма тестов | Wall класса | Тестов |
|---|---|---|---|
| ServiceBooking.Tests.Tests.Cycle18TrialLifecycleTaskTests | 3:28.1 | 3:24.6 | 14 |
| ServiceBooking.Tests.Tests.Cycle33UnifiedPushTests | 3:16.5 | 2:55.6 | 18 |
| ServiceBooking.Tests.Tests.CompaniesTests | 3:16.0 | 3:11.7 | 85 |
| ServiceBooking.Tests.Tests.ClientNotePhotosTests | 3:09.6 | 3:10.9 | 21 |
| ServiceBooking.Tests.Tests.Cycle23ShopsCatalogTests | 3:08.8 | 3:06.2 | 28 |
| ServiceBooking.Tests.Tests.MastersTests | 3:01.1 | 2:54.4 | 19 |
| ServiceBooking.Tests.Tests.Cycle25StaffMaxTests | 2:52.3 | 2:33.7 | 19 |
| ServiceBooking.Tests.Tests.BookingsFlowSmokeTests | 2:50.8 | 2:31.7 | 63 |
| ServiceBooking.Tests.Tests.NotificationChannelsTests | 2:49.4 | 2:48.1 | 22 |
| ServiceBooking.Tests.Tests.Cycle28ShowcaseGuardsTests | 2:49.3 | 2:48.3 | 11 |
| ServiceBooking.Tests.Tests.Cycle25CustomerTests | 2:46.0 | 2:18.4 | 8 |
| ServiceBooking.Tests.Tests.ReviewsTests | 2:45.4 | 2:38.4 | 17 |
| ServiceBooking.Tests.Tests.Cycle18TrialPlanTests | 2:42.8 | 2:37.9 | 36 |
| ServiceBooking.Tests.Tests.Cycle26CompanyCardTests | 2:42.6 | 2:33.3 | 27 |
| ServiceBooking.Tests.Tests.Cycle17ClientCancelTests | 2:41.7 | 2:33.9 | 15 |
| ServiceBooking.Tests.Tests.Cycle24HoursAcceptanceTests | 2:37.7 | 2:30.6 | 28 |
| ServiceBooking.Tests.Tests.Cycle24PickupTests | 2:36.8 | 2:29.4 | 16 |
| ServiceBooking.Tests.Tests.Cycle15ClientRescheduleTests | 2:31.4 | 2:19.9 | 12 |
| ServiceBooking.Tests.Tests.Cycle25PickListTests | 2:29.3 | 2:22.9 | 5 |
| ServiceBooking.Tests.Tests.Cycle24TariffTests | 2:28.9 | 2:18.1 | 12 |

## vitest (203 файлов, pool forks)

Фазы (суммы по воркерам): transform 8.2 s, setup 20.8 s, collect 33.6 s, tests 53.9 s, environment 1:08.3, prepare 8.9 s.

| Файл | Время | Тесты | Накладные | Среда |
|---|---|---|---|---|
| goods/src/pages/cabinet/CreateShopPage.test.tsx | 3.1 s | 3.1 s | 0.0 s | jsdom |
| src/pages/owner/SettingsTab.qa32.test.tsx | 2.0 s | 2.0 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/LinkPage.test.tsx | 1.8 s | 1.8 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.test.tsx | 1.4 s | 1.4 s | 0.0 s | jsdom |
| src/pages/admin/BillingAccountsAdminTab.test.tsx | 1.2 s | 1.2 s | 0.0 s | jsdom |
| goods/src/components/storefront/CartPanel.test.tsx | 1.1 s | 1.1 s | 0.0 s | jsdom |
| src/components/booking/BookingModal.showcase.test.tsx | 1.0 s | 1.0 s | 0.0 s | jsdom |
| src/pages/admin/PlansTab.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/CatalogPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/RegisterPage.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| src/pages/owner/SalonProfileSection.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| goods/src/components/catalog/ProductModal.test.tsx | 0.9 s | 0.9 s | 0.0 s | jsdom |
| goods/src/pages/StorefrontPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| goods/src/components/profile/ShopProfileSection.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/MasterClientsPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/SubjectRequestPage.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/admin/NoticesAdminTab.test.tsx | 0.8 s | 0.8 s | 0.0 s | jsdom |
| src/pages/owner/CompanyManagePage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| src/components/notifications/ChannelRequestModal.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
| goods/src/pages/cabinet/HoursPage.test.tsx | 0.7 s | 0.7 s | 0.0 s | jsdom |
