| Эндпоинт | SQL/запрос до → после | Строк из БД до → после | КБ из БД до → после | p50 мс до → после | Δ p50 | p95 мс до → после | Δ p95 |
|---|---|---|---|---|---|---|---|
| masters/clients p1 | 6 → 7 | 6,654 → 2,533 | 4,583 → 252 | 265.5 → 25.9 | -90% | 319.0 → 37.5 | -88% |
| masters/clients p60 (middle) | 6 → 7 | 6,654 → 2,454 | 4,583 → 243 | 263.7 → 21.0 | -92% | 329.0 → 26.6 | -92% |
| masters/clients search phone | 6 → 7 | 6,654 → 2,443 | 4,583 → 241 | 263.4 → 23.8 | -91% | 319.3 → 29.7 | -91% |
| masters/clients search name | 6 → 7 | 6,654 → 2,480 | 4,583 → 248 | 261.2 → 22.2 | -92% | 311.6 → 26.0 | -92% |
| company stats 1 month | 5 → 8 | 4,751 → 65 | 1,616 → 4 | 59.4 → 34.4 | -42% | 72.7 → 41.9 | -42% |
| company stats 1 year | 5 → 8 | 24,030 → 399 | 17,871 → 14 | 812.4 → 50.0 | -94% | 891.4 → 57.9 | -94% |
| reports/masters 1 month | 9 → 8 | 1,269 → 1,268 | 792 → 167 | 21.7 → 13.0 | -40% | 33.2 → 18.4 | -45% |
| reports/masters 1 year | 9 → 8 | 15,249 → 15,248 | 9,504 → 1,976 | 267.9 → 29.6 | -89% | 326.1 → 51.0 | -84% |
| bookings/master 1 day | 5 → 4 | 13 → 12 | 13 → 10 | 7.5 → 6.6 | -12% | 11.3 → 10.1 | -10% |
| bookings/master 1 month | 5 → 4 | 559 → 558 | 425 → 292 | 25.3 → 19.7 | -22% | 34.4 → 26.4 | -23% |
| bookings/client (~150) | 8 → 7 | 165 → 164 | 237 → 237 | 13.8 → 14.6 | +6% | 18.1 → 20.1 | +11% |
| bookings/{id} (master) | 6 → 5 | 8 → 7 | 9 → 7 | 6.4 → 6.7 | +5% | 9.6 → 9.6 | +0% |
| bookings/availability 30d (anon) | 6 → 6 | 130 → 130 | 11 → 11 | 6.9 → 5.1 | -27% | 13.1 → 7.2 | -45% |
| bookings/slots 1 day (anon) | 5 → 5 | 5 → 5 | 1 → 1 | 5.5 → 3.8 | -31% | 8.4 → 6.2 | -27% |
| bookings/slots 1 day (client JWT) | 8 → 7 | 8 → 7 | 3 → 2 | 7.1 → 4.8 | -32% | 9.9 → 7.7 | -22% |
| admin/companies p1 | 7 → 6 | 88 → 42 | 31 → 11 | 6.7 → 5.3 | -21% | 9.2 → 8.1 | -13% |
| admin/bookings (no filter, top 500) | 3 → 2 | 562 → 561 | 730 → 729 | 94.5 → 83.3 | -12% | 115.1 → 96.5 | -16% |
| admin/bookings company+month | 3 → 2 | 561 → 560 | 731 → 730 | 38.1 → 32.5 | -15% | 50.4 → 43.5 | -14% |
| admin/notification-channels p1 | 4 → 10 | 272 → 82 | 89 → 23 | 5.2 → 7.3 | +39% | 6.9 → 10.8 | +57% |
| profile (trivial auth, F20) | 3 → 3 | 5 → 5 | 1 → 2 | 2.9 → 3.5 | +20% | 4.8 → 5.9 | +23% |
| legal/documents (anon baseline) | 0 → 0 | 0 → 0 | 0.0 → 0.0 | 0.9 → 1.0 | +12% | 1.1 → 1.2 | +15% |

Прогоны по отдельности (p50, мс): run1 до/после | run2 до/после
- masters/clients p1: 266.2/31.6 | 264.8/20.2
- masters/clients p60 (middle): 269.1/20.9 | 258.3/21.0
- masters/clients search phone: 269.4/26.7 | 257.5/21.0
- masters/clients search name: 264.3/22.2 | 258.1/22.1
- company stats 1 month: 59.7/34.7 | 59.1/34.1
- company stats 1 year: 814.7/51.3 | 810.1/48.6
- reports/masters 1 month: 23.4/13.4 | 20.1/12.7
- reports/masters 1 year: 271.0/30.3 | 264.8/28.9
- bookings/master 1 day: 7.3/6.8 | 7.7/6.4
- bookings/master 1 month: 27.0/20.6 | 23.7/18.8
- bookings/client (~150): 13.7/16.0 | 13.8/13.1
- bookings/{id} (master): 6.3/6.8 | 6.5/6.6
- bookings/availability 30d (anon): 7.0/4.7 | 6.8/5.4
- bookings/slots 1 day (anon): 5.7/3.5 | 5.3/4.0
- bookings/slots 1 day (client JWT): 7.2/4.3 | 7.0/5.4
- admin/companies p1: 6.5/5.0 | 6.8/5.6
- admin/bookings (no filter, top 500): 100.3/85.4 | 88.7/81.2
- admin/bookings company+month: 40.0/32.8 | 36.1/32.2
- admin/notification-channels p1: 4.8/6.6 | 5.6/8.0
- profile (trivial auth, F20): 2.6/3.5 | 3.2/3.6
- legal/documents (anon baseline): 0.7/0.9 | 1.1/1.1

create: before {'n': 20, 'stmts_median': 44.0, 'stmts_all': [44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 44], 'roundtrips_median': 38.0, 'ms_median_via_proxy': 34.69599350046337}, after {'n': 20, 'stmts_median': 39.0, 'stmts_all': [39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39, 39], 'roundtrips_median': 33.0, 'ms_median_via_proxy': 30.27448749890027}
