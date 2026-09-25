# Ретроспективная проверка `revoke-preview` — готовая к выполнению процедура

**Основание:** `LEGAL_REVIEW_CYCLE16.md` §5.3, требование О9. **Дата подготовки: 25.09.2026.**

⚠️ **Почему это процедура, а не отчёт.** Проверку не удалось выполнить из сессии: обращения к боевой
машине блокируются ограничением среды («Production Reads»), при том что ключи доступа в `~/.ssh` есть и
хост `ezbook.ru` описан в `~/.ssh/config`. Ниже — точный набор команд, рассчитанный на один проход.
Результат вносится в `INCIDENT_REVIEW_CYCLE16.md` (создаётся по итогам).

🔴 **Правила исполнения, обязательные:**
1. **Только чтение.** Ни одной команды, меняющей состояние: никаких рестартов, правок конфигурации,
   `docker compose up/down`.
2. **Бэкапы не разворачивать.** Шаг 4 ниже — только перечисление копий. Разворачивание копии боевой базы
   — отдельное решение с собственными рисками, принимается отдельно.
3. **Память машины.** Свободно ~1 ГБ, 492 МиБ в подкачке (`CURRENT_STATE.md` §9 P0 п. 1). Журналы читать
   потоково (`grep` по потоку), не загружать файлы целиком, не поднимать контейнеров.
4. **ПДн в отчёт не копировать.** Идентификация только по `userId`. Ни телефонов, ни содержимого заметок,
   ни текстов обращений.
5. **«Не найдено» ≠ «не могло сохраниться».** Эти два вывода записываются РАЗДЕЛЬНО (см. шаг 5).

---

## Шаг 0. Подключение и точка отсчёта

```bash
ssh ezbook
date -u +"%Y-%m-%dT%H:%M:%SZ"   # зафиксировать момент начала проверки в отчёте
```

## Шаг 1. Популяция потенциально затронутых субъектов

Аккаунты с неподтверждённым номером, на чей номер существуют гостевые сущности. Запрос только на чтение
(он же в `ARCHITECTURE_CYCLE16.md` §245.6, здесь расширен до разбивки по типам).

```bash
docker compose -f /opt/ezbook/app/docker-compose.prod.yml --env-file /opt/ezbook/app/.env \
  exec -T postgres psql -U postgres -d servicebooking -c "
SELECT count(*) AS affected_accounts,
       count(*) FILTER (WHERE has_health) AS with_health_notes
FROM (
  SELECT u.\"Id\",
         EXISTS (SELECT 1 FROM \"ClientHealthNotes\" h WHERE h.\"GuestPhone\" = u.\"PhoneNumber\") AS has_health
  FROM \"AspNetUsers\" u
  WHERE u.\"PhoneNumber\" IS NOT NULL
    AND u.\"DeletedAtUtc\" IS NULL
    AND NOT EXISTS (SELECT 1 FROM \"VerifiedPhones\" v
                    WHERE v.\"UserId\" = u.\"Id\" AND v.\"Phone\" = u.\"PhoneNumber\")
    AND (EXISTS (SELECT 1 FROM \"Bookings\" b          WHERE b.\"GuestPhone\" = u.\"PhoneNumber\")
      OR EXISTS (SELECT 1 FROM \"ClientNotes\" n       WHERE n.\"GuestPhone\" = u.\"PhoneNumber\")
      OR EXISTS (SELECT 1 FROM \"ClientHealthNotes\" h WHERE h.\"GuestPhone\" = u.\"PhoneNumber\"))
) t;"
```

**Если `affected_accounts = 0`** — дальнейшие шаги можно не выполнять, но записать это надо явно:
популяция пуста, значит эксплуатировать было нечего. Это самый желательный исход.

## Шаг 2. Кандидаты на разбор: аккаунт зарегистрирован ПОЗЖЕ появления данных на номере

Юрист отдельно оговаривает: это **не доказательство** — законный владелец номера тоже мог
зарегистрироваться позже. Это сужение множества для шага 3.

```bash
docker compose -f /opt/ezbook/app/docker-compose.prod.yml --env-file /opt/ezbook/app/.env \
  exec -T postgres psql -U postgres -d servicebooking -c "
SELECT u.\"Id\" AS user_id,
       u.\"CreatedAtUtc\" AS registered,
       LEAST(
         COALESCE((SELECT min(b.\"CreatedAtUtc\") FROM \"Bookings\" b    WHERE b.\"GuestPhone\" = u.\"PhoneNumber\"), 'infinity'),
         COALESCE((SELECT min(n.\"CreatedAtUtc\") FROM \"ClientNotes\" n WHERE n.\"GuestPhone\" = u.\"PhoneNumber\"), 'infinity')
       ) AS first_guest_data
FROM \"AspNetUsers\" u
WHERE u.\"PhoneNumber\" IS NOT NULL AND u.\"DeletedAtUtc\" IS NULL
  AND NOT EXISTS (SELECT 1 FROM \"VerifiedPhones\" v WHERE v.\"UserId\" = u.\"Id\" AND v.\"Phone\" = u.\"PhoneNumber\")
ORDER BY registered DESC;"
```

⚠️ Имена столбцов дат (`CreatedAtUtc`) сверить с фактической схемой — если отличаются, поправить, а не
пропускать шаг.

## Шаг 3. Обращения к затронутым эндпоинтам

Четыре маршрута (проверено по коду `ProfileController.cs`):

| Маршрут | Метод | Что делает |
|---|---|---|
| `/api/profile/export` | GET | чтение, включая расшифрованные сведения о здоровье |
| `/api/profile/delete-account` | POST | физическое уничтожение |
| `/api/profile/consents/revoke` | POST | отзыв согласия + уничтожение |
| `/api/profile/consents/revoke-preview` | GET | раскрытие ЧИСЛА чужих заметок о здоровье |

```bash
# nginx: есть ли обращения вообще и когда
sudo zgrep -hE "(GET|POST) /api/profile/(export|delete-account|consents/revoke)" \
  /var/log/nginx/access.log* | awk '{print $4, $6, $7, $9}' | sort | uniq -c | sort -rn | head -50

# журнал приложения: те же маршруты с userId (потоково, без загрузки файла целиком)
docker compose -f /opt/ezbook/app/docker-compose.prod.yml logs --no-color --since 2160h api 2>/dev/null \
  | grep -E "profile/(export|delete-account|consents/revoke)" | head -200
```

Сопоставить найденные `userId` со списком из шага 2. **Пересечение — это и есть кандидат на факт.**

## Шаг 4. Признаки уничтожения

`ClientHealthNotes` удаляются физически, прямого следа в БД нет. Косвенные признаки:

```bash
# записи об отзыве согласия — есть ли сделанные с аккаунтов из шага 2
docker compose -f /opt/ezbook/app/docker-compose.prod.yml --env-file /opt/ezbook/app/.env \
  exec -T postgres psql -U postgres -d servicebooking -c "
SELECT \"Id\", \"CreatedAtUtc\", \"DocumentKey\", \"Action\"
FROM \"ConsentRecords\"
WHERE \"Action\" = 'Revoked'
ORDER BY \"CreatedAtUtc\" DESC LIMIT 100;"

# бэкапы — ТОЛЬКО перечислить, не разворачивать
sudo ls -la /var/backups/servicebooking/ | head -40
```

## Шаг 5. Глубина познания — обязательно к записи

```bash
# фактическая глубина журналов nginx
sudo ls -la /var/log/nginx/ | head -20
# фактическая глубина журнала контейнера
docker inspect --format '{{.LogPath}}' $(docker compose -f /opt/ezbook/app/docker-compose.prod.yml ps -q api) \
  | xargs sudo ls -la
```

Записать в отчёт **по каждому источнику** самую раннюю доступную дату. Политика заявляет 90 дней для
журналов и 30 для копий (п. 13.2) — проверить, совпадает ли это с фактом.

Формулировка вывода — дословно в этой форме, без смягчения:

> Проверено с DD.MM.YYYY по 25.09.2026. За пределами этого периода установить факт обращения к
> затронутым эндпоинтам **невозможно** — журналы не сохранились.

---

## Что делать с результатом

| Исход | Действие |
|---|---|
| Популяция пуста (шаг 1 = 0) | записать в реестр инцидентов, факт не установлен, часы не идут |
| Обращений не найдено в пределах глубины | записать **оба** вывода раздельно: «не найдено в пределах N дней» + «за пределами установить невозможно» |
| 🔴 Найдено хотя бы одно пересечение шага 3 со списком шага 2 | **ФАКТ УСТАНОВЛЕН. С этого момента идут 24 часа** (уведомление РКН: инцидент, предполагаемая причина, предполагаемый вред, принятые меры, уполномоченное лицо) **и 72 часа** (результаты внутреннего расследования). Уведомлять надо **и салоны** — они операторы заметок о здоровье (п. 14.4 Политики). Решение принимается с практикующим юристом, не техническим специалистом |

**Запись в реестре инцидентов делается в любом случае** — состав полей в `LEGAL_REVIEW_CYCLE16.md` §5.4.
Пункт 14.1 Политики уже утверждает, что оператор ведёт такой учёт; если реестра нет, утверждение
недостоверно, и это отдельный риск.
