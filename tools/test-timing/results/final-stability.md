# final: стабильность функционального набора (QA-36-03)

Коммит замера `6723a9f` и далее (см. колонку), P=4 если не указано, colima 2 CPU / 4 ГБ, одна машина, чужих прогонов нет.
Тестов в каждом прогоне 1267.

| Прогон | Семя порядка | P | Wall | Результат |
|---|---|---|---|---|
| final-1..3 | 360001 | 4 | 3:09.8 / 3:12.8 / 3:10.3 | зелёные |
| stab | 360002 | 4 | 3:39.2 | зелёный |
| stab | 360003 | 4 | 3:15.6 | красный: `LegalConsentVersionChangeTests.EditorialChange_NeverBlocks_ButShowsBanner` (409 в `RegisterAsync`) |
| stab | 360004 | 4 | 3:14.5 | зелёный |
| stab | 360005 | 4 | 3:15.5 | красный: `NotificationDispatchExtraTests.Budget_InterruptedPass_...` (`ownCallCount()` 3 вместо 2) |
| p1 | 360006 | 1 | 3:30.3 | зелёный |
| rep | 360003 | 4 | 3:10.1 | зелёный (повтор семени, не воспроизвелось) |
| rep | 360005 | 4 | 3:10.1 | зелёный (повтор семени, не воспроизвелось) |
| rep | 360008 | 4 | 3:12.3 | зелёный |
| routes-after (`--route-log`) | 360007 | 4 | 3:16.0 | красный: `LegalConsentVersionChangeTests.ReplacingDraftWithVettedText_...` (409 в `RegisterAsync`) |
| fix (после правки `RegisterAsync`) | 360009, 360010, 360011 | 4 | 3:18.7 / 3:18.6 / 3:32.0 | зелёные |

Vitest при заполненном `VITE_SMARTCAPTCHA_SITEKEY`: 3 прогона по 1536 тестов, зелёные (19 / 20 / 22 с).
