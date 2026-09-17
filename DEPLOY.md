# Деплой ServiceBooking на ezbook.ru — по шагам

Этот документ — раннбук: открываете раздел, выполняете команды сверху вниз, сверяетесь со
строкой «Должно получиться». Всё объяснительное (зачем именно так, а не иначе) вынесено в конец,
в раздел [«Почему так сделано»](#почему-так-сделано) — на него есть ссылки по ходу текста, но для
самого выполнения шагов читать его не обязательно.

Все команды выполняются по SSH на целевой машине, если явно не сказано «на вашей локальной
машине».

**Как копировать команды.** Копируйте из самого файла, а не из пересланного сообщения или превью
в мессенджере: они часто оборачивают «голые» ссылки в угловые скобки (`<https://…>`) и рвут
переносы строк. В bash `<` — это перенаправление ввода, поэтому такая вставка падает с
`syntax error near unexpected token`. Если увидели эту ошибку — первым делом посмотрите, не
появились ли вокруг URL угловые скобки; больше в команде менять ничего не нужно.

## Целевая машина (факты, зафиксированы заранее)

| Параметр | Значение |
|---|---|
| ОС | Ubuntu 24.04 desktop (GNOME/GDM), не серверная установка |
| Диск | один раздел `/dev/sda4` на `/`, 210 ГБ всего, ~180 ГБ свободно, LUKS нет |
| Локальный адрес | `192.168.0.93` |
| Внешний адрес | статический белый IP, проброс 80/443 настроен и проверен |
| Домен | `ezbook.ru`, A-записи уже указывают на белый IP |
| Занятые порты | 22 (sshd), 8443 (чужое `fleetservice.service`, **не трогать**), 3389/3390 (remote desktop), 631 (CUPS, localhost), 53 (localhost) |
| Свободные порты | 80, 443, 5000, 5432 |
| Что не установлено | Docker, nginx, certbot, PostgreSQL на хосте — ставим с нуля в §1 |
| ufw | включён; 22, 80, 8443, 3389, 3390 были разрешены изначально, **443 открыт 2026-09-16** в ходе настройки — проверьте `sudo ufw status`, если строки `443/tcp ALLOW` нет, откройте (§1) |

Порты 22, 80 и 443 проверены снаружи реальными запросами 2026-09-16: SSH отвечает настоящим
рукопожатием, 80 и 443 доходят до машины. Обычные проверки «открыт ли порт» на этом адресе
бесполезны — провайдер принимает TCP-соединения на любой порт, включая заведомо закрытые, и
всегда отвечает «открыт». Проверять можно только реальным слушателем и реальным запросом.

**На этой машине разворачивается ветка `develop`, не `master`.** Почему — см.
[«Почему так сделано» → какую ветку катим»](#какую-ветку-катим-и-почему).

---

## Чек-лист всей установки

Порядок работ — сверху вниз, без прыжков назад:

1. §0 — проверить DNS и способ входа по SSH
2. §1 — поставить Docker, nginx, certbot, открыть 443 в ufw, отключить sleep
3. §2 — склонировать `develop`, разложить правовые документы
4. §3 — заполнить `.env`
5. §4 — поднять backend + Postgres в Docker
6. §5 — выложить фронтенд первый раз
7. §6 — настроить nginx, получить TLS-сертификат
8. §7 — проверить, что сайт открылся и правовые документы в нужном состоянии
9. §8 — завести пользователя `ezbookdeploy` для деплоя по кнопке
10. §9 — завести секреты в GitHub Actions, включить деплой кнопкой
11. §10 — как деплоить и откатывать код после первичной установки
12. §11 — настроить автоматический бэкап
13. §12 — поднять GlitchTip и мониторинг доступности
14. §13 — что означают ответы health-check
15. §14 — часовые пояса (справочно)
16. §15 — если что-то пошло не так
17. §16 — чек-лист «Первый запуск на этой машине» (непроверенное вживую)

---

## 0. Перед началом

**Шаг 0.1.** Проверьте, что DNS всё ещё указывает туда, куда нужно (записи мог кто-то поменять
после первой проверки):

```bash
dig +short ezbook.ru
dig +short www.ezbook.ru
curl -s https://ifconfig.me
```

Должно получиться: все три значения совпадают. Если нет — не запускайте certbot (§6), сначала
поправьте DNS: HTTP-01 идёт именно на IP из A-записи.

**Шаг 0.2.** Уточните у заказчика имя пользователя для входа по SSH (на десктопной Ubuntu обычно
не `root`, а обычный пользователь с `sudo`) и подтверждение, что порт 22 проброшен наружу так же,
как 80/443 (см. «Непроверенное» выше).

---

## 1. Подготовка машины

Пакеты — через `apt`. На хосте включён `ufw` (см. шаг 1.4). SELinux в Ubuntu нет — это
debian-семейство, там AppArmor, он этому стеку не мешает и трогать его не нужно.

Если вы вошли не под `root`, добавляйте `sudo` перед командами ниже — везде, кроме `docker ...`
после того, как ваш пользователь попадёт в группу `docker` (шаг 1.2).

**Не трогайте на этой машине**: `fleetservice.service` (чужое приложение на 8443),
GDM/gnome-remote-desktop (3389/3390), CUPS (631), системный `/usr/bin/python3`. Ни один шаг ниже
их не задевает.

**Шаг 1.1.** Войдите и обновите систему:

```bash
ssh <пользователь>@<IP-или-домен-машины>
sudo apt update && sudo apt upgrade -y
```

**Шаг 1.2.** Поставьте Docker официальным скриптом Docker. Он подключает официальный репозиторий
Docker и ставит оттуда `docker-ce`, `containerd`, плагины `buildx` и `compose` — то есть ровно то,
что нам нужно, но одной строкой (почему именно официальный Docker, а не snap, не `apt install
docker.io` и не `podman-docker`, см. [«Почему так сделано» → Docker не из snap»](#docker-не-из-snap)):

```bash
curl -fsSL https://get.docker.com | sudo sh
```

Затем добавьте себя в группу `docker` и включите автозапуск:

```bash
sudo usermod -aG docker "$USER" && sudo systemctl enable --now docker
```

**Перелогиньтесь** (`exit`, затем снова `ssh`) — без этого членство в группе `docker` не подхватится
в текущей сессии, и дальнейшие шаги будут требовать `sudo` там, где раннбук его не ставит.

Должно получиться:

```bash
docker --version && docker compose version
```

Обе команды выводят версию. Если `docker compose version` ругается «is not a docker command» —
не встал плагин compose; поставьте отдельно: `sudo apt install -y docker-compose-plugin`.

<details>
<summary>Если предпочитаете подключить репозиторий вручную</summary>

Результат тот же, источник тот же. Этот вариант даёт больше контроля, но команды многострочные,
с подстановками и переносами — при копировании через мессенджер или markdown-просмотрщик они рвутся
(перенос теряется, URL оборачивается в `<…>`, и bash падает с `syntax error near unexpected token`).
Если пошли этим путём и `apt install docker-ce` говорит «has no installation candidate» — значит
репозиторий не подключился; проверьте `ls -l /etc/apt/keyrings/docker.gpg` и
`cat /etc/apt/sources.list.d/docker.list`, при необходимости удалите оба файла и вернитесь к скрипту
выше.

```bash
sudo apt install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

</details>

**Шаг 1.3.** Поставьте nginx (пакет Ubuntu, конфиги — в `/etc/nginx/sites-available/` +
`sites-enabled/`, используется в §6):

```bash
sudo apt install -y nginx
sudo systemctl enable --now nginx
```

Должно получиться: `nginx -v` отрабатывает без ошибок.

**Шаг 1.4.** Поставьте certbot через snap (так рекомендует сам проект Certbot для Ubuntu — свежее,
чем apt-пакет):

```bash
sudo snap install core; sudo snap refresh core
sudo snap install --classic certbot
sudo ln -sf /snap/bin/certbot /usr/bin/certbot
```

Должно получиться: `certbot --version` отрабатывает без ошибок.

**Шаг 1.5.** Поставьте прочее и прогоните общую проверку:

```bash
sudo apt install -y git nano
docker --version && docker compose version && nginx -v && certbot --version
```

Должно получиться: все четыре команды выводят версию, ни одна не падает с ошибкой.

**Шаг 1.6.** Убедитесь, что порт 443 открыт в ufw. Изначально его в разрешённом списке не было;
443 открыли 2026-09-16 при настройке, так что команда ниже, скорее всего, скажет «Skipping adding
existing rule» — это нормально (почему это важно и как диагностировать, если забыли, см.
[«Почему так сделано» → ufw»](#ufw-включён-и-443-нужно-открыть-явно)):

```bash
sudo ufw allow 443/tcp
sudo ufw status verbose
```

Должно получиться: в выводе `sudo ufw status verbose` присутствуют строки `22`, `80`, `443/tcp`,
`8443`, `3389`, `3390` со статусом `ALLOW`. Больше в файрволе ничего менять не нужно: 22 и 80 уже
разрешены, 8443 соседа не трогаем, API и Postgres наружу не смотрят вовсе.

**Шаг 1.7.** Отключите переход хоста в sleep/suspend — это десктопная сборка Ubuntu, по умолчанию
она засыпает по таймауту бездействия, что для работающего сервиса недопустимо:

```bash
sudo systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target
```

Должно получиться:

```bash
systemctl status sleep.target   # ожидаем "masked"
```

Графическую сессию (GDM/GNOME) при этом трогать не нужно — она работает в фоне независимо от того,
залогинен ли кто-то на экране, и никак не мешает Docker/nginx.

Первичная настройка машины на этом закончена. Сборку фронтенда на самой машине не ставим —
об этом отдельно в §5.

---

## 2. Клонирование репозитория и правовые документы

**Шаг 2.1.** Склонируйте `develop` (не `master`, см. предупреждение в начале файла):

```bash
sudo mkdir -p /opt/ezbook
sudo chown "$USER" /opt/ezbook
cd /opt/ezbook
git clone https://github.com/Douk771/ServiceBooking.git app
cd app
git checkout develop
git pull
```

Если репозиторий приватный — заведите deploy-key или HTTPS-токен, это разовая настройка доступа.

**Шаг 2.2.** Разложите правовые документы — `docker-compose.prod.yml` монтирует каталог `./legal`
рядом с собой поверх черновика, запечённого в образ:

```bash
mkdir -p /opt/ezbook/app/legal
cp /opt/ezbook/app/ServiceBooking.API/App_Data/legal/*.* /opt/ezbook/app/legal/
```

Черновая версия на этом шаге — это заводское состояние репозитория, и запускаться с ней в
production разрешено осознанно (см.
[«Почему так сделано» → черновая редакция правовых текстов»](#старт-с-черновой-редакцией-правовых-текстов)).
Замена текста после юридической вычитки — уже не первичная настройка, а штатная эксплуатационная
операция:

```bash
scp privacy.html <пользователь>@<IP-или-домен-машины>:/opt/ezbook/app/legal/privacy.html
# на сервере отредактируйте /opt/ezbook/app/legal/legal.json: новая version (без "-draft", если
# текст финальный), effectiveFrom, isDraft:false, changeKind: "Material" или "Editorial"
```

Пересборка и рестарт контейнера не требуются — провайдер сам перечитывает `legal.json` в течение
`Legal:ReloadSeconds` (30 c по умолчанию). `changeKind: "Material"` разлогинивает всех, кто ещё не
принял новую редакцию; `"Editorial"` — нет.

---

## 3. Секреты (`.env`)

**Шаг 3.1.**

```bash
cd /opt/ezbook/app
cp .env.production.example .env
nano .env
```

Заполните поля:

| Поле | Как получить | Обязательность |
|---|---|---|
| `POSTGRES_PASSWORD` | `openssl rand -base64 24` | обязательно |
| `JWT_KEY` | `openssl rand -base64 48`, минимум 32 символа, не равно плейсхолдеру из `appsettings.json` | обязательно, иначе контейнер не стартует |
| `SUPERADMIN_PHONE` | ваш номер, формат `+7XXXXXXXXXX` — станет суперадмином при первом запуске | обязательно |
| `SUPERADMIN_PASSWORD` | пароль суперадмина, не равен плейсхолдеру `Admin12345` | обязательно, иначе контейнер не стартует |
| `SMARTCAPTCHA_SECRET_KEY` / `SMARTCAPTCHA_SITE_KEY` | кабинет Yandex Cloud SmartCaptcha | обязательно |
| `FORWARDEDHEADERS__TRUSTEDNETWORKS__0` | подсеть docker-моста, по умолчанию `172.16.0.0/12` (ARCHITECTURE.md §9.1) | обязательно, пустое значение — контейнер не стартует |
| `SENTRY_DSN` | из GlitchTip, см. §12 | необязательно |
| `GIT_SHA` | заполняется автоматически `deploy/deploy-remote.sh` | руками не трогать |

Сохраните (Ctrl+O, Enter, Ctrl+X в nano). `.env` не попадёт в git — он в `.gitignore`.

**Шаг 3.2.** Проверьте подстановку переменных (контейнеры не поднимает, только печатает итоговый
конфиг):

```bash
docker compose -f docker-compose.prod.yml --env-file .env config
```

Должно получиться: `Jwt__Key` и `SuperAdmin__Password` в выводе — реальные значения, не пустые
строки. Если один из них плейсхолдер или отсутствует, API откажется стартовать в §4 (fail-fast —
это специально сделано так, а не тихий запуск с `Admin12345`).

---

## 4. Backend + Postgres в Docker

**Шаг 4.1.**

```bash
cd /opt/ezbook/app
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

**Шаг 4.2.** Проверка:

```bash
docker compose -f docker-compose.prod.yml ps
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5000/api/companies
```

Должно получиться: `200`. Swagger в Production не поднимается вовсе — `404` на
`.../swagger/index.html` это не признак проблемы.

Если `curl` не вернул `200` — посмотрите логи и пришлите вывод для разбора:

```bash
docker compose -f docker-compose.prod.yml logs api --tail=100
```

**Шаг 4.3.** Настройте бэкап загруженных файлов сейчас же — два независимых docker volume под
изображения:

| Volume | Что лежит | Как раздаётся |
|---|---|---|
| `api_uploads` | логотипы компаний, аватары, картинки услуг (публичные) | напрямую через `/uploads/...` |
| `api_private_uploads` | фото к заметкам о клиентах (персональные данные) | только через `GET /api/client-notes/photos/{id}`, с проверкой прав |

Бэкапьте оба, но `api_private_uploads` требует того же уровня защиты, что и сама база — это
персональные данные. Автоматический бэкап настраивается в §11; разовая команда вручную:

```bash
# Имя тома = <имя проекта compose>_<имя тома>. Имя проекта — это имя каталога, в котором лежит
# docker-compose.prod.yml, то есть `app` (/opt/ezbook/app). Не «ezbook» — на этом легко ошибиться,
# и ошибка тихая: docker молча СОЗДАСТ пустой том с неверным именем вместо того, чтобы упасть.
# Сверьтесь перед выполнением: docker volume ls | grep api_
PROJECT="${SERVICEBOOKING_COMPOSE_PROJECT:-app}"
docker run --rm -v "${PROJECT}_api_uploads:/from" -v "${PROJECT}_api_private_uploads:/from-private" \
  -v /opt/ezbook/backups:/to alpine \
  tar czf /to/uploads-$(date +%F).tar.gz -C / from from-private
```

Каталоги `Storage__PrivateRoot`/`Storage__PublicRoot` уже прописаны в `docker-compose.prod.yml` —
менять не нужно. Если когда-нибудь понадобится их переопределить, см. ограничения в
[«Почему так сделано»](#storage-root).

---

## 5. Сборка фронтенда

Фронтенд собирается в CI (`.github/workflows/ci.yml`, джоб `frontend`), не на самой машине — на
сервере нет `nodejs`/`npm`, и так и должно оставаться.

Раскладка на диске — релизы плюс симлинк, который переключается атомарно:

```
/var/www/ezbook/
├── releases/
│   ├── 20260915T101500Z/     ← распакованный dist конкретного релиза
│   └── 20260914T190000Z/     ← хранятся 3 последних, старые чистит deploy-remote.sh сам
└── current -> releases/20260915T101500Z    ← симлинк, nginx `root` указывает сюда (см. §6)
```

**Шаг 5.1.** Создайте каталог под релизы (один раз — дальше и GitHub Actions §9, и
`deploy/deploy.sh` §10.4 сами создают под ним конкретный релиз при каждом деплое):

```bash
sudo mkdir -p /var/www/ezbook/releases
sudo chown -R "$USER" /var/www/ezbook
```

`/var/www` принадлежит `root`, поэтому без `sudo` первая команда упадёт с `Permission denied`.
Владельца сразу отдаём вам — иначе следующий шаг не сможет туда писать. Позже, в §8, каталог
получит общую группу `ezbook`, чтобы в него мог писать и пользователь деплоя.

**Шаг 5.2.** Убедитесь, что переменная капчи `VITE_SMARTCAPTCHA_SITEKEY` (публичный site-key, не
секрет) задана один раз в CI: GitHub → Settings → Secrets and variables → Actions → **Variables**
→ `VITE_SMARTCAPTCHA_SITEKEY`. Vite подставляет его во время `npm run build` в CI.

**Шаг 5.3.** Первый деплой фронтенда делается либо кнопкой «Run workflow» → Deploy staging
(develop) в GitHub Actions (настройка — §9, использование — §9.2), либо запасным путём —
`./deploy/deploy.sh` с локальной машины (§10.4). Заводить пользователя `ezbookdeploy` (§8) нужно
раньше, чем нажимать кнопку — оба способа деплоя по кнопке требуют его.

После первого деплоя проверка:

```bash
ls -la /var/www/ezbook/current/index.html
```

Должно получиться: файл существует через симлинк.

---

## 6. nginx + HTTPS

**Шаг 6.1.** Разложите конфиг сайта (в `sites-available` + симлинк в `sites-enabled` — конвенция
пакета nginx Ubuntu, не кладите файл прямо в `conf.d`):

```bash
sudo cp /opt/ezbook/app/deploy/nginx/ezbook.conf /etc/nginx/sites-available/ezbook.conf
sudo ln -sf /etc/nginx/sites-available/ezbook.conf /etc/nginx/sites-enabled/ezbook.conf
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx
```

Должно получиться: `nginx -t` печатает `syntax is ok` / `test is successful`.

**Шаг 6.2.** Выпустите сертификат (certbot сам допишет server-блок на 443 и настроит редирект с
80; HTTP-01 ходит на `ezbook.ru:80` снаружи — именно поэтому важно, чтобы порт 80 оставался
проброшен и открыт и после этого шага, см.
[«Почему так сделано» → порт 80 после выпуска сертификата»](#порт-80-нужен-и-после-выпуска-сертификата)):

```bash
sudo certbot --nginx -d ezbook.ru -d www.ezbook.ru
```

Certbot спросит email и согласие с условиями — ответьте на вопросы в терминале.

**Шаг 6.3.** Проверьте автопродление — snap-пакет certbot сам ставит системный таймер при
установке, вручную настраивать не нужно:

```bash
systemctl list-timers | grep -i certbot   # ожидаем snap.certbot.renew.timer
sudo certbot renew --dry-run              # холостой прогон, без реальной замены сертификата
```

Если таймера нет (проверьте `snap services certbot` — на случай нестандартной установки), тогда и
только тогда добавьте свой:

```bash
echo "0 3 * * * root certbot renew --quiet --deploy-hook 'systemctl reload nginx'" \
  | sudo tee /etc/cron.d/certbot-renew
```

**Шаг 6.4.** Security-заголовки уже прописаны в `deploy/nginx/ezbook.conf` (HSTS, `nosniff`,
`Referrer-Policy`, CSP, `X-Frame-Options`). Ничего дополнительно настраивать не нужно, но
проверьте живьём после выпуска сертификата — эта проверка не формальность: 2026-09-17 она уже
один раз нашла реальный дефект на этой же машине (`/embed/` отдавал `X-Frame-Options`/CSP, хотя не
должен был — причина и фикс в [«Почему так сделано» → фикс `/embed/` fallback от
2026-09-17»](#фикс-embed-fallback-от-2026-09-17)):

```bash
curl -I https://ezbook.ru/                | grep -Ei 'strict-transport|x-content-type|referrer-policy|content-security|x-frame'
curl -I https://ezbook.ru/embed/anything  | grep -Ei 'x-frame|frame-ancestors'   # должно быть ПУСТО
```

И вручную, глазами, в браузере: форма записи с капчей открывается и отправляется, карточка
клиента с загруженным фото открывает миниатюру и полноразмер. Если меняете CSP в будущем — эти
две живые проверки обязательны перед выкладкой, детали (почему именно эти два места самые хрупкие
под CSP) — в
[«Почему так сделано» → CSP»](#csp-два-самых-хрупких-места).

---

## 7. Проверка после первого разворачивания

**Шаг 7.1.** Откройте `https://ezbook.ru` в браузере — должна открыться главная страница.

**Шаг 7.2.** Зарегистрируйте тестовый аккаунт, войдите под номером из `SUPERADMIN_PHONE` —
должна появиться ссылка «Админ» в шапке.

**Шаг 7.3.** Проверьте, какая редакция правовых документов опубликована:

```bash
cat /opt/ezbook/app/legal/legal.json | grep -E 'version|isDraft'
```

Если `isDraft: true` — это заводское состояние, запускаться с ним разрешено (см. §2.2), но
убедитесь, что это осознанный выбор, а не забытый шаг. Если правовая вычитка уже прошла —
обновите текст по §2.2 до первых регистраций.

Первичное разворачивание на этом закончено. Дальше — пользователь для деплоя по кнопке (§8) и
сама кнопка (§9).

---

## 8. Пользователь `ezbookdeploy` — для деплоя по кнопке из GitHub Actions

GitHub-раннер (облачный, не self-hosted) подключается к этой машине по SSH снаружи, через тот же
22-й порт, что и вы сами. Секреты приложения (`.env`) в GitHub не попадают никогда — они живут
только в `.env` на машине (§3). GitHub знает только то, без чего SSH-подключение невозможно:
приватный ключ, адрес хоста, имя пользователя.

Зачем нужен именно отдельный пользователь, а не root и не ваш личный, и какой остаточный риск при
этом принимается — см.
[«Почему так сделано» → пользователь ezbookdeploy»](#пользователь-ezbookdeploy-не-root).

**Шаг 8.1.** Создайте системного пользователя без пароля:

```bash
sudo useradd --create-home --shell /bin/bash ezbookdeploy
sudo passwd -l ezbookdeploy              # вход только по ключу, никогда по паролю
sudo usermod -aG docker ezbookdeploy     # docker compose без sudo
```

**Шаг 8.2.** Заведите общую группу с вашим пользователем — оба должны иметь право писать в
каталоги приложения:

```bash
sudo groupadd ezbook || true
sudo usermod -aG ezbook ezbookdeploy
sudo usermod -aG ezbook "$USER"          # перелогиньтесь после этого
sudo chgrp -R ezbook /opt/ezbook/app /var/www/ezbook
sudo chmod -R g+rwX /opt/ezbook/app /var/www/ezbook
sudo find /opt/ezbook/app /var/www/ezbook -type d -exec chmod g+s {} \;
```

**Шаг 8.3.** Установите форс-команд скрипт — владелец `root`, `ezbookdeploy` не может его
отредактировать:

```bash
sudo cp /opt/ezbook/app/deploy/ssh-deploy-wrapper.sh /usr/local/sbin/ezbook-deploy-wrapper.sh
sudo chown root:root /usr/local/sbin/ezbook-deploy-wrapper.sh
sudo chmod 755 /usr/local/sbin/ezbook-deploy-wrapper.sh
```

**Шаг 8.4.** Сгенерируйте SSH-ключ деплоя (на вашей локальной машине или в CI) и добавьте его
публичную половину сюда с ограничениями — приватная половина уйдёт в секрет GitHub
`DEPLOY_SSH_KEY` (§9.1):

```bash
sudo -u ezbookdeploy mkdir -p ~ezbookdeploy/.ssh
sudo -u ezbookdeploy chmod 700 ~ezbookdeploy/.ssh
echo 'command="/usr/local/sbin/ezbook-deploy-wrapper.sh",no-agent-forwarding,no-port-forwarding,no-X11-forwarding,no-pty ssh-ed25519 AAAA...ВАШ_ПУБЛИЧНЫЙ_КЛЮЧ github-actions-deploy' \
  | sudo -u ezbookdeploy tee -a ~ezbookdeploy/.ssh/authorized_keys
sudo -u ezbookdeploy chmod 600 ~ezbookdeploy/.ssh/authorized_keys
```

**Шаг 8.5.** Выдайте точечный `sudo` — НЕ `NOPASSWD:ALL`. Используйте `visudo`, не редактируйте
файл напрямую — иначе ошибка синтаксиса может заблокировать `sudo` вообще для всех:

```bash
sudo visudo -f /etc/sudoers.d/ezbook-deploy
```

Содержимое файла:

```
ezbookdeploy ALL=(root) NOPASSWD: /usr/sbin/nginx -t
ezbookdeploy ALL=(root) NOPASSWD: /usr/bin/systemctl reload nginx
```

Если пути к бинарям на вашей машине отличаются (`which nginx`, `which systemctl`) — поправьте и
здесь, и синхронно в `deploy-remote.sh`/`rollback.sh`, которые вызывают эти же команды.

**Шаг 8.6.** Проверка:

```bash
sudo -u ezbookdeploy sudo -n /usr/sbin/nginx -t                              # должно пройти без пароля
sudo -u ezbookdeploy sudo -n /usr/bin/systemctl status fleetservice.service  # должно ОТКАЗАТЬ
```

Должно получиться: первая команда отрабатывает без запроса пароля, вторая — отказывает
(`ezbookdeploy` не в sudoers ни на один юнит, кроме `reload nginx`). Это же — один из пунктов
чек-листа «Первый запуск на этой машине» (§16).

---

## 9. GitHub Actions — секреты и кнопка «Deploy»

Предпосылка: §8 уже выполнен.

**Шаг 9.1.** Заведите секреты и переменные — GitHub → Settings → Secrets and variables → Actions:

| Имя | Тип | Значение |
|---|---|---|
| `DEPLOY_SSH_KEY` | Secret | приватная половина ключа из §8.4 — весь файл, включая `BEGIN`/`END` |
| `DEPLOY_USER` | Secret | `ezbookdeploy` |
| `DEPLOY_HOST` | Secret | `ezbook.ru` (или белый IP машины) |
| `DEPLOY_HOST_KEY` | Variable | вывод `ssh-keyscan -H ezbook.ru` (см. ниже); необязательно, но настоятельно рекомендуется |
| `VITE_SMARTCAPTCHA_SITEKEY` | Variable | тот же site-key, что уже используется в `ci.yml` (§5.2) |

Получить `DEPLOY_HOST_KEY` (с любой доверенной машины, не обязательно с раннера):

```bash
ssh-keyscan -H ezbook.ru
```

Скопируйте вывод целиком (1–3 строки) в переменную `DEPLOY_HOST_KEY`.

Секреты приложения (`.env` на машине) в GitHub не заводятся — воркфлоу они не нужны.

**Шаг 9.2.** Настройте GitHub Environments (Settings → Environments):

- `staging` — без обязательных ревьюеров, деплой стенда должен оставаться быстрым и
  автоматическим по нажатию;
- `production` — включите **Required reviewers** (хотя бы себя). `deploy-production.yml`
  ссылается на `environment: production` — без этой настройки в репозитории защита не действует.

**Шаг 9.3.** Как запускать:

Стенд (`develop`): Actions → Deploy staging (develop) → Run workflow → ветка `develop` → Run.

Прод (только после релиза — решение о релизе см. в
[«Почему так сделано» → какую ветку катим»](#какую-ветку-катим-и-почему)): Actions → Deploy production (tag on
master) → Run workflow → в дропдауне «Use workflow from» выберите **тег**, не ветку → введите
`deploy` в поле подтверждения → Run. Воркфлоу сам откажет, если выбранный ref — не тег или тег не
лежит на `master`.

Результат — в самой вкладке Actions: `docker compose ps` в конце, либо «ДЕПЛОЙ НЕУСПЕШЕН.» с
готовой командой отката.

**Шаг 9.4.** Откат после деплоя по кнопке — тот же `bash deploy/rollback.sh` руками на машине
(§10.2). Воркфлоу не откатывает автоматически при неудаче — решение принимает человек.

**Единая целевая машина.** Оба workflow сейчас нацелены на одну и ту же физическую машину —
второй, отдельной прод-машины пока нет. Пока не объявлен первый релиз, «прод»-воркфлоу технически
способен катить на то же железо, где стенд, если кто-то создаст тег раньше времени; единственная
защита сейчас — организационная (тег не создаётся до решения о релизе) плюс required reviewer.
Появится отдельная прод-машина — заведите отдельные `DEPLOY_HOST`/`DEPLOY_USER`/`DEPLOY_SSH_KEY`
в environment-scoped секретах `production`.

---

## 10. Обновление кода после первичной установки

С этого цикла деплой не делается голым `git pull` на сервере — `deploy-remote.sh` требует
аргументом номер релиза, каталог которого уже должен существовать под
`/var/www/ezbook/releases/`. Кто-то должен сначала собрать фронтенд и положить его туда — это
делает либо GitHub Actions (§9, основной способ), либо запасной путь — локальный
`deploy/deploy.sh` (§10.4).

**Что делает прогон** (`deploy/deploy-remote.sh <release-timestamp>`):

1. Тегирует текущий образ API `servicebooking-api:previous`, запоминает текущий релиз-симлинк в
   `/var/www/ezbook/.previous` — это всё, что нужно для отката.
2. Атомарно переключает `/var/www/ezbook/current` на новый релиз, `nginx -t && reload`.
3. `docker compose up -d --build` — пересобирает и перезапускает контейнер API.
4. Ждёт `/api/health/ready` (таймаут 120 с). Если не дождался — печатает `ДЕПЛОЙ НЕУСПЕШЕН.` и
   следующей строкой готовую команду отката.
5. Подчищает старые релизы (оставляет 3 последних).

**Шаг 10.1.** Проверить вручную, без нового деплоя:

```bash
curl -s https://ezbook.ru/api/health/ready
```

**Шаг 10.2.** Откат — одна команда, без аргументов:

```bash
cd /opt/ezbook/app
bash deploy/rollback.sh
```

Откатывает и фронтенд (симлинк `current` → предыдущий релиз), и backend (образ `:previous` →
`:latest`, без пересборки — поэтому быстро), ждёт `readiness`, печатает статус контейнеров.
Необязательный аргумент — метка более старого релиза (`bash deploy/rollback.sh
20260913T120000Z`) для отката фронтенда на две и более версии назад; backend-образ всё равно
откатывается на единственный сохранённый `:previous`.

Прогон «выкатили → проверили → откатили → проверили» выполнен на локальном стенде 2026-09-09. На
этой машине с реальным nginx/systemd этот прогон нужно повторить один раз перед тем, как
полагаться на процедуру в бою — см. чек-лист §16.

**Шаг 10.3. Откат кода после применённой миграции.** Миграции применяются автоматически при
старте API и `rollback.sh` их не откатывает — «откатить миграцию» не всегда вообще возможно без
потери данных.

Если после деплоя выяснилось, что новый код сломан, а миграция из этого же релиза уже накатилась:

1. Спросите: старый код совместим со схемой после миграции? Если миграция чисто аддитивна (новая
   колонка/таблица, старый код её не знает) — да, `rollback.sh` полностью безопасен.
2. Если миграция удалила колонку/таблицу, которую старый код читает — откат кода без плана по
   данным делать нельзя: сначала пишется и применяется вручную обратная миграция (`dotnet ef
   migrations add`, `Down()`), и только потом `rollback.sh`.
3. Если обратной миграции нет и данные уже частично в новой форме — это инцидент, не рутинная
   операция: сначала свежий бэкап (§11), потом решение вручную, не по шаблону.

**Шаг 10.4. Запасной путь — деплой с локальной машины (Rider или руками), без GitHub Actions.**
Используйте здесь тот же ограниченный ключ/пользователя `ezbookdeploy` из §8, а не отдельный
личный ключ с полным шеллом — иначе весь смысл ограничений §8 исчезает.

```bash
# один раз: GitHub CLI на вашей локальной машине
brew install gh   # или см. https://cli.github.com для другой ОС
gh auth login

# один раз: тот же ключ ezbookdeploy, что уже сгенерирован для §8.4/9.1
# если его ещё нет на этой локальной машине — сгенерируйте так же, как в §8.4, и добавьте
# публичную часть в тот же authorized_keys на машине (та же command=... строка)
ssh-keygen -t ed25519 -C "deploy-ezbook-local"

# проверка — должна пройти без пароля и вернуть ответ health-check, не echo ok
ssh -i <путь-к-приватному-ключу> ezbookdeploy@<IP-или-домен-машины> "health"

# один раз: локальный конфиг
cp .deploy.env.example .deploy.env
# отредактируйте SSH_HOST=ezbookdeploy@ezbook.ru (тот же ezbookdeploy, не личный пользователь)

# сам деплой
./deploy/deploy.sh
```

Если `ssh ... "health"` просит пароль — ключ не добавлен в `authorized_keys` или на машине
отключён вход по ключу (`PubkeyAuthentication` в `sshd_config`), разбирайте по `ssh -v`. Если
пароль не просит, но ответ `refused: ...` — форс-команд скрипт не установлен или установлен не
туда (§8.3), проверьте путь в `command=`.

Кнопка в Rider (необязательно, если запускаете `./deploy/deploy.sh` руками): Run → Edit
Configurations… → + → Shell Script → Name `Deploy to VPS`, Script path `deploy/deploy.sh`,
Working directory — корень проекта → OK. Дальше запуск конфигурации (▶ / Shift+F10) — то же
самое, что `./deploy/deploy.sh`, вывод — в панели Run.

---

## 11. Бэкап и восстановление

### 11.1. Настройка автоматического бэкапа

Бэкап делает `deploy/backup/backup.sh` на самом хосте (не сайдкар-контейнер), запускается
systemd-таймером, не cron'ом.

**Шаг 11.1.1.**

```bash
mkdir -p /opt/ezbook/app  # если ещё не создан (см. §2)
sudo cp /opt/ezbook/app/deploy/backup/servicebooking-backup.service /etc/systemd/system/
sudo cp /opt/ezbook/app/deploy/backup/servicebooking-backup.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now servicebooking-backup.timer
```

Должно получиться:

```bash
systemctl list-timers servicebooking-backup.timer
systemctl status servicebooking-backup.timer
```

**Что делает каждый прогон:**

1. Проверяет свободное место (нужно ≥ 1,5× размера прошлой копии) — не хватает, ничего не пишет.
2. `pg_dump -Fc` базы.
3. `tar czf` обоих docker volume, без остановки приложения: `api_uploads` и
   `api_private_uploads`.
4. Ротация: 7 ежесуточных + 4 еженедельных (воскресенье).
5. Всё пишется в `/var/backups/servicebooking` (вне любого docker volume, права `0700`) —
   `docker compose down -v` не унесёт копии вместе с оригиналами.
6. Итог — в `journalctl -u servicebooking-backup`; неуспех дополнительно шлёт событие в GlitchTip
   (§12), если `SENTRY_DSN` задан.

Посмотреть историю прогонов:

```bash
journalctl -u servicebooking-backup --no-pager -n 50
ls -la /var/backups/servicebooking
```

Бэкап хранится только локально, на этой же машине — что это защищает и от чего нет, см.
[«Почему так сделано» → локальный бэкап»](#бэкап-только-локальный).

### 11.2. Восстановление — по шагам, без раздумий

**Выполняйте из-под root** (`sudo -i`): каталог `/var/backups/servicebooking` принадлежит root
с правами `0700`, и под обычным пользователем подстановки `$(ls -t /var/backups/...)` ниже вернут
пустоту — команда «отработает» без файла и без ошибки.

Занимает 10–15 минут при разумном размере базы.

```bash
cd /opt/ezbook/app

# 1. Остановить API (Postgres можно оставить)
docker compose -f docker-compose.prod.yml --env-file .env stop api

# 2. Восстановить базу из последнего дампа
LATEST_DB=$(ls -t /var/backups/servicebooking/db-*.dump | head -1)
docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
  pg_restore -U postgres -d servicebooking --clean --if-exists < "$LATEST_DB"

# 3. Восстановить оба volume (ВНИМАНИЕ: перезаписывает их текущее содержимое)
LATEST_UPLOADS=$(ls -t /var/backups/servicebooking/uploads-*.tar.gz | head -1)
LATEST_PRIVATE=$(ls -t /var/backups/servicebooking/private-uploads-*.tar.gz | head -1)
# Тот же префикс, что использует backup.sh (SERVICEBOOKING_COMPOSE_PROJECT, по умолчанию `app`).
# ПРОВЕРЬТЕ имена перед выполнением — при опечатке docker создаст пустой том, а не откажет,
# и восстановление «пройдёт успешно», не восстановив ничего:
docker volume ls | grep api_
PROJECT="${SERVICEBOOKING_COMPOSE_PROJECT:-app}"
docker run --rm -v "${PROJECT}_api_uploads:/dst" -v /var/backups/servicebooking:/src alpine \
  sh -c "rm -rf /dst/* && cd /dst && tar xzf /src/$(basename "$LATEST_UPLOADS")"
docker run --rm -v "${PROJECT}_api_private_uploads:/dst" -v /var/backups/servicebooking:/src alpine \
  sh -c "rm -rf /dst/* && cd /dst && tar xzf /src/$(basename "$LATEST_PRIVATE")"

# 4. Запустить API и дождаться готовности
docker compose -f docker-compose.prod.yml --env-file .env up -d api
curl -s https://ezbook.ru/api/health/ready
```

**Шаг 5, обязательный.** Приватное фото должно открываться через API, а не просто «файл на диске
есть» — это единственная проверка, доказывающая, что строки БД и файлы совпали. Войдите под
учётной записью персонала компании, откройте карточку клиента с фото, убедитесь, что миниатюра и
полный размер загружаются (либо `curl` с Bearer-токеном на `GET
/api/client-notes/photos/{id}`, если фото/id уже известны).

Фактический прогон выполнен на локальном тестовом стенде 2026-09-09: создана компания, заметка
клиента с фото, фото открывалось через API (200, `image/jpeg`), стенд полностью уничтожен
(`docker compose down -v`), база и оба volume восстановлены из бэкапа с нуля, то же фото снова
открылось с тем же содержимым. На этой машине этот прогон стоит повторить один раз вручную (с
реальными `systemd`/`pg_dump`) и дописать дату сюда и в чек-лист §16 — локальный стенд
подтверждает механику команд, но не заменяет проверку на целевой ОС с реальным объёмом данных.

---

## 12. GlitchTip и мониторинг

GlitchTip живёт на отдельном поддомене (`errors.ezbook.ru`), не на самом `ezbook.ru` — так
проще выдать ему собственный TLS-сертификат и отдельный периметр (basic-auth), не трогая
`deploy/nginx/ezbook.conf`. Шаги 12.1–12.6 — разовая настройка; 12.7 и дальше — то, что вы будете
использовать регулярно.

**Шаг 12.1.** Заведите A-запись `errors.ezbook.ru` на тот же адрес, что и `ezbook.ru` (у
регистратора/DNS-провайдера домена), и проверьте, что она разошлась:

```bash
dig +short errors.ezbook.ru
curl -s https://ifconfig.me
```

Должно получиться: оба значения совпадают. Если нет — не запускайте certbot (шаг 12.4 ниже),
DNS ещё не готов. Отдельно открывать порт в `ufw` не нужно: GlitchTip идёт через тот же 443,
что и основной сайт (`443/tcp` уже открыт — см. §1), только на другое имя хоста.

**Шаг 12.2.** Допишите в `.env` (тот же файл, что и §3) переменные GlitchTip:

| Поле | Как получить | Обязательность |
|---|---|---|
| `GLITCHTIP_POSTGRES_PASSWORD` | `openssl rand -base64 24` — отдельный пароль от `POSTGRES_PASSWORD` приложения, у GlitchTip своя, не связанная с ним база | обязательно |
| `GLITCHTIP_SECRET_KEY` | `openssl rand -base64 48` — ключ подписи сессий/CSRF Django, не должен совпадать с `JWT_KEY` приложения | обязательно, иначе контейнер `web`/`worker` не стартует |
| `GLITCHTIP_DOMAIN` | `errors.ezbook.ru` — **без** `https://`, схему уже дописывает `docker-compose.glitchtip.yml` | обязательно, иначе GlitchTip генерирует ссылки на `localhost` |
| `GLITCHTIP_EMAIL_URL` | см. шаг 12.2.1 ниже | необязательно, но без него алертов на почту не будет |
| `GLITCHTIP_FROM_EMAIL` | адрес отправителя, например `glitchtip@ezbook.ru` | необязательно (по умолчанию `glitchtip@localhost`, большинство почтовых серверов такое письмо отбракуют) |
| `SENTRY_DSN` | из GlitchTip — заполняется позже, на шаге 12.6 | необязательно |

**Шаг 12.2.1. Ловушка с почтой.** Если `GLITCHTIP_EMAIL_URL` не задан, действует значение по
умолчанию из `docker-compose.glitchtip.yml` — `consolemail://`. Это не «не настроено, но
работает», это письма, которые GlitchTip честно пишет в **лог контейнера** (`docker compose -f
docker-compose.glitchtip.yml logs web`) и больше никуда не отправляет — то есть уведомлений об
ошибках на почту не будет вообще, при этом сам GlitchTip не сообщает об этом никак, кроме этой
строчки в логе. Если заказчик хочет алерты на почту (шаг 12.9), нужен обычный SMTP-ящик — формат:

```
GLITCHTIP_EMAIL_URL=smtp://<логин>:<пароль-приложения>%40<то-что-после-собаки-если-есть>@<smtp-хост>:<порт>
```

Пример для Яндекс.Почты (`smtp.yandex.ru:465`) или Mail.ru (`smtp.mail.ru:465`) — оба требуют
**пароль приложения**, не обычный пароль от почты (создаётся в настройках безопасности ящика,
отдельно для «внешнего клиента/SMTP»; обычный пароль от такого ящика по протоколу SMTP не
примет ни тот, ни другой провайдер). Спецсимволы пароля приложения (если есть) нужно
URL-кодировать — `@` → `%40` и т.д. Какой конкретно ящик использовать, решает заказчик; когда он
определится, конкретное значение уходит в `.env` тем же способом, что и остальные секреты (§3), в
git не попадает.

> **Про basic-auth и приём событий.** Пароль закрывает только человеческий вход. Пути приёма
> событий (`/api/<id>/store/`, `/envelope/`, `/security/`) вынесены в отдельный блок с
> `auth_basic off` — иначе Sentry-клиент внутри API, который про пароль не знает, получал бы 401
> на каждое событие, и трекер оставался бы пустым. Выглядело бы это как «ошибок не было».
> Аутентификация там своя — по ключу из DSN, и это эндпоинт записи, прочитать через него нельзя.

**Шаг 12.3.** Защита входа — два независимых слоя, не один вместо другого:

1. Открытая саморегистрация в GlitchTip уже отключена в самом файле
   (`ENABLE_OPEN_USER_REGISTRATION: "false"` в `docker-compose.glitchtip.yml`) — без этого шага
   первый же человек, дошедший до `/auth/register/`, мог бы сам себе завести аккаунт с доступом
   ко всем issue, включая чужие трейсы с фрагментами пользовательских данных.
2. Basic-auth на уровне nginx (`deploy/nginx/errors.ezbook.conf`) — GlitchTip показывает
   продакшен-ошибки, то есть потенциально фрагменты персональных данных из запросов, поэтому до
   собственной формы логина GlitchTip анонимный интернет вообще не должен достучаться.
   Заведите файл пароля (`apache2-utils` даёт `htpasswd`, в Ubuntu он не стоит по умолчанию):

```bash
sudo apt install -y apache2-utils
sudo htpasswd -c /etc/nginx/errors.ezbook.htpasswd admin
```

`-c` создаёт файл заново — используйте его только один раз; для второго и последующих
пользователей то же без `-c`, иначе первый пароль потрётся.

**Шаг 12.4.** Разложите nginx-конфиг и получите сертификат — та же последовательность, что и в
§6, для второго server_name:

```bash
sudo cp /opt/ezbook/app/deploy/nginx/errors.ezbook.conf /etc/nginx/sites-available/errors.ezbook.conf
sudo ln -sf /etc/nginx/sites-available/errors.ezbook.conf /etc/nginx/sites-enabled/errors.ezbook.conf
sudo nginx -t
sudo systemctl reload nginx
sudo certbot --nginx -d errors.ezbook.ru
```

Должно получиться: `nginx -t` печатает `syntax is ok` / `test is successful`, certbot дописывает
блок на 443 и редирект с 80 в тот же файл (тот же механизм, что в §6.2). Порт 80 нужен и после
выпуска сертификата — по той же причине, что для основного домена, см. [«Почему так сделано» →
порт 80 после выпуска сертификата»](#порт-80-нужен-и-после-выпуска-сертификата), автопродление
идёт тем же snap-таймером (§6.3), отдельно настраивать не нужно.

**Шаг 12.5.** Поднимите GlitchTip:

```bash
cd /opt/ezbook/app
docker compose -f docker-compose.glitchtip.yml --env-file .env up -d
```

> **Машина не видит себя через белый адрес.** Роутер не разворачивает трафик внутрь сети
> (hairpin NAT), поэтому обращение к `errors.ezbook.ru` и `ezbook.ru` изнутри уходит наружу и не
> возвращается. Это ломает отправку событий: и приложение, и `backup.sh`, и `health-alert.sh`
> шлют их на публичное имя. Молча — Sentry-клиент не падает из-за недоступного трекера, он теряет
> события, и панель выглядит пустой, как будто ошибок нет.
> Для контейнера приложения это решено в `docker-compose.prod.yml` (`extra_hosts` с
> `host-gateway`). Для скриптов на хосте добавьте имена в `/etc/hosts` **один раз**:
>
> ```bash
> echo "127.0.0.1 ezbook.ru errors.ezbook.ru" | sudo tee -a /etc/hosts
> ```
>
> Проверка: `curl -sI https://errors.ezbook.ru/ | head -1` на самой машине должна вернуть
> `HTTP/1.1 401`, а не пустоту.

> **`/opt/ezbook/app` принадлежит деплою — не редактируйте его руками.** Рабочее дерево в
> `/opt/ezbook/app` ведёт деплой: каждый прогон принудительно приводит его ровно к выкатываемому
> коммиту (`git checkout --force` + `git clean -fd` в `deploy/ssh-deploy-wrapper.sh`), независимо от
> того, что там лежит. Это осознанное решение, а не дефект: раньше деплой падал посреди прогона —
> уже после того, как фронтенд был залит, — с `error: ... would be overwritten by checkout`, если на
> машине руками была правка отслеживаемого файла или неотслеживаемый файл на пути, который тоже
> появлялся в целевом коммите. Теперь деплой не падает — он молча (для оператора — не молча: прогон
> печатает `git status --short` того, что отбрасывает, ДО переключения, это видно в логе прогона
> GitHub Actions) отбрасывает любые ручные правки дерева и приводит его к коммиту. Из этого следует:
>
> - **любая правка прямо в `/opt/ezbook/app` не переживёт следующий деплой** — не полагайтесь на неё
>   как на постоянную; `.env` и `/legal/` — исключение, они не в git (см. `.gitignore`), поэтому
>   `git clean -fd` (без `-x`) их не трогает никогда;
> - чтобы **доставить файл на машину не через полный деплой**, не пишите в дерево — берите файл, не
>   трогая его:
>
>   ```bash
>   cd /opt/ezbook/app && git fetch
>   git show origin/develop:deploy/nginx/errors.ezbook.conf | sudo tee /etc/nginx/sites-available/errors.ezbook.conf > /dev/null
>   ```
>
> - чтобы **аварийная правка не потерялась**, вносите её обычным путём — закоммитьте в репозиторий и
>   выкатите деплоем, а не правьте `/opt/ezbook/app` на месте; следующий прогон всё равно сотрёт
>   локальную правку, а закоммиченная останется.

> **Про имя проекта.** В `docker-compose.glitchtip.yml` задано `name: glitchtip`. Без этого Docker
> берёт имя каталога (`app`) — то же, что у стека приложения, — и сервисы с совпадающими именами
> схлопываются: GlitchTip остаётся без своей базы, а `docker compose ... down` для одного стека
> сносит контейнеры другого. Команды GlitchTip всегда выполняйте с этим compose-файлом; проверить,
> что стеки разведены, можно по префиксам в `docker ps`: `app-*` — приложение, `glitchtip-*` — трекер.

Миграции применяет одноразовый сервис `migrate`, и `web` стартует только после его успешного
завершения. Если стек поднимался ДО добавления этого сервиса (или `migrate` почему-то не
отработал), логин отдаёт 500 с `relation "users_user" does not exist` — схемы базы просто нет.
Разовое лечение:

```bash
cd /opt/ezbook/app
docker compose -f docker-compose.glitchtip.yml --env-file .env run --rm web ./manage.py migrate
docker compose -f docker-compose.glitchtip.yml --env-file .env restart web
```

Первый вход — `https://errors.ezbook.ru/` (браузер сначала спросит basic-auth логин/пароль из
шага 12.3, потом покажет собственную форму логина GlitchTip). Создайте организацию и проект (тип
Node/Other — нужен только DSN). Скопируйте DSN проекта: Settings → Client Keys (DSN).

**Шаг 12.6.** Подключите приложение:

```bash
# в .env на машине
SENTRY_DSN=https://<key>@errors.ezbook.ru/<project_id>
```

```bash
docker compose -f docker-compose.prod.yml --env-file .env up -d api   # применить переменную
```

Пустое значение — приложение работает без трекера, это нормальное состояние до тех пор, пока
GlitchTip не поднят. `backup.sh` использует тот же `SENTRY_DSN` для алерта о неуспешном бэкапе —
заполнили один раз, получили оба канала алертов.

**Шаг 12.7.** Проверка сквозной цепочки: спровоцируйте тестовую ошибку (или дождитесь
органической 500) — в GlitchTip на `https://errors.ezbook.ru/` должно появиться ровно одно новое
issue. Если ничего не появилось, по уровням: `curl -I https://errors.ezbook.ru/` отвечает (иначе
проблема в nginx/certbot, шаг 12.4) → `docker compose -f docker-compose.glitchtip.yml logs web`
не молчит про ошибку приёма события → `SENTRY_DSN` в `.env` API совпадает с DSN проекта в
GlitchTip дословно (шаг 12.6). Ретенция событий — 30 дней (`GLITCHTIP_MAX_EVENT_LIFE_DAYS` в
`docker-compose.glitchtip.yml`).

**Шаг 12.8.** Настройте уведомления: `Settings → Notifications`, e-mail заказчика (работает,
только если `GLITCHTIP_EMAIL_URL` заполнен реальным SMTP — шаг 12.2.1, иначе письма уходят в лог
контейнера и никуда больше). Что вызывает алерт (и только это — остальное тихое):

| Повод | Откуда |
|---|---|
| Новая, ранее не встречавшаяся ошибка | GlitchTip «new issue», автоматически |
| Всплеск известной ошибки | Правило в `Settings → Alerts` — настройте под свою нагрузку |
| Неуспешный прогон бэкапа | `backup.sh` шлёт событие сам (§11.1 п. 6) |
| `/api/health/ready` не отвечает 3 проверки подряд | `deploy/monitor/health-alert.sh`, §12.9 |

Дневная сводка — встроенный digest GlitchTip (`Settings → Notifications → Daily digest`).

Если вся машина недоступна — GlitchTip недоступен вместе с ней, алерта не будет: мониторинг
снаружи машины в объём этого раннбука не входит.

**Шаг 12.9.** Поднимите монитор доступности (алерт на долгий 503):

```bash
chmod +x /opt/ezbook/app/deploy/monitor/health-alert.sh
sudo cp /opt/ezbook/app/deploy/monitor/health-alert.service /etc/systemd/system/
sudo cp /opt/ezbook/app/deploy/monitor/health-alert.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now health-alert.timer
```

Опрашивает `/api/health/ready` раз в 5 минут; при трёх подряд неудачах шлёт событие в GlitchTip
тем же DSN. Тоже на хосте, тоже systemd — работает даже если сам API-контейнер лежит.

---

## 13. Health-check — что означают ответы

```bash
curl -s https://ezbook.ru/api/health/ready
```

| Ответ | Значит |
|---|---|
| `{"status":"Healthy"}` | можно деплоить, можно давать нагрузку |
| `{"status":"Unhealthy","failed":"database"}` | БД недоступна ИЛИ остались непринятые миграции — смотрите `docker compose logs api` |
| нет ответа / connection refused / таймаут | процесс не поднялся, либо nginx не может до него достучаться — сначала `docker compose ps`, потом `docker compose logs api` |

`GET /api/health/live` (без обращения к БД) используется `healthcheck:` контейнера `api` — если
он не подтверждает «Healthy» 3 раза подряд, `docker compose ps` покажет контейнер как
`unhealthy`. `start_period: 60s` рассчитан на то, что миграции при старте могут занять время — не
паникуйте, если первые секунды после `up -d --build` контейнер отвечает не сразу.

---

## 14. Часовые пояса (справочно)

Все контейнеры (Postgres, API) работают в UTC — это не настраивается отдельно, `TZ=...` не
задаётся ни в одном compose-файле, и так и должно оставаться.

У продукта нет понятия часового пояса компании: время визита (`Booking.Date`/`StartTime`) — это
местное время салона, введённое персоналом буквально, без конвертации между часовыми поясами.
Это ограничение продукта, а не баг (`Company.TimeZoneId` в модели данных отсутствует осознанно,
ARCHITECTURE.md §5.4) — если сеть компаний в разных часовых поясах когда-нибудь понадобится, это
отдельный цикл работ.

---

## 15. Если что-то пошло не так

Пришлите вывод одной из этих команд — разберём по выводу:

```bash
docker compose -f docker-compose.prod.yml logs api --tail=100
sudo nginx -t
journalctl -u nginx --no-pager --tail=50
sudo certbot certificates

# SELinux в Ubuntu нет — причина 502/403 без явной строки в логах не SELinux, не тратьте время
# на ausearch/getenforce. Проверьте вместо этого:
sudo journalctl -xeu docker --no-pager --tail=50   # сам демон Docker
ss -tulpn | grep -E ':80|:443|:5000|:8443'         # кто и что реально слушает
sudo ufw status verbose                            # не режет ли ufw то, что должно ходить
```

---

## 16. Первый запуск на этой машине — что проверить вживую

Раннбук описывает механику команд, но часть из них ни разу не прогонялась на реальной машине —
только на локальном стенде или по статическому разбору конфига. Отметьте каждый пункт (дата, кто
проверил, результат) прямо здесь, когда сделано:

- [x] **Скачивание выгрузки данных под новым CSP (`blob:` + `<a download>`), в браузере, с
      открытой консолью.** Откройте DevTools → Console и Network до клика, скачайте выгрузку,
      убедитесь, что файл действительно скачался и в консоли нет `Refused to...` по CSP.
      **2026-09-17: ПРОШЛО.** Файл выгрузки скачивается, содержимое непустое, в консоли браузера
      ни одной строки `Refused to`. Проверены все три чувствительных к CSP сценария, а не только
      скачивание: выгрузка через `blob:` + `<a download>`, форма записи со SmartCaptcha (внешний
      домен в `script-src`/`frame-src`), карточка клиента с приватным фото (`img-src ... blob:`).
      Шрифты Google Fonts грузятся — это подтверждает `style-src`/`font-src`, добавленные devops
      после живой проверки headless-браузером.
- [x] **GlitchTip end-to-end** (§12). **2026-09-17: ПРОШЛО, цепочка «событие → трекер → письмо»
      замкнута.** Проверка дошла до письма в ящике, а не до «issue появилось» — и это оправдалось:
      по пути нашлись и исправлены **пять** дефектов, каждый из которых оставлял систему внешне
      работоспособной и молча нерабочей:
      (1) в компоузе не было сервиса миграций — схема БД не создавалась, логин отдавал 500
      `relation "users_user" does not exist`;
      (2) basic-auth стоял на всём поддомене, включая пути приёма событий — приложение получало бы
      401 на каждое событие, а панель выглядела бы как «ошибок нет»;
      (3) оба стека делили имя compose-проекта `app`, контейнеры `postgres` схлопнулись, GlitchTip
      остался без своей базы (симптом — очень медленная панель, а не ошибка), и `down` для одного
      стека снёс бы контейнеры другого;
      (4) роутер не разворачивает трафик внутрь сети, поэтому машина не видела себя по публичному
      имени — Sentry-клиент, `backup.sh` и `health-alert.sh` теряли бы события молча;
      (5) в проекте GlitchTip не было правила оповещения — события копились, письма не уходили.
      Отдельно: письмо приходит на адрес учётной записи в панели, а не на ящик-отправитель.
- [x] **Откат одной командой** (`deploy/rollback.sh`, §10.2) на реальном nginx и systemd этой
      машины. **2026-09-17: ПРОШЛО.** Два релиза → `bash deploy/rollback.sh` без аргументов →
      симлинк `current` переключился на предыдущий релиз, сайт и `/api/health/ready` здоровы →
      выкатка вперёд по кнопке вернула систему на актуальный коммит. Проверена именно обратимость,
      а не только откат: откат, после которого нельзя выкатиться обратно, — тупик, а не откат.
      Попутно вскрылась и устранена хрупкость: `git checkout <sha>` в форс-команде падал от любого
      постороннего файла в `/opt/ezbook/app`, причём уже ПОСЛЕ загрузки фронтенда, оставляя машину
      в промежуточном состоянии. Переключение сделано принудительным (коммит `88f1ed2`).
      Прежний прогон на локальном стенде — прогон 2026-09-09 был на локальном стенде. Сделать два последовательных деплоя,
      затем `rollback.sh` без аргументов, убедиться, что `readiness` подтверждается после
      отката, дописать дату сюда и в §10.2.
- [x] **Восстановление из бэкапа** (§11.2) на этой машине.
      **2026-09-17: ПРОШЛО.** Сценарий: заведена компания и заметка о клиенте с фотографией,
      снят бэкап (`systemctl start servicebooking-backup.service`), заметка удалена через
      интерфейс, выполнено восстановление по §11.2 — заметка и фотография вернулись, фото
      открывается. Приложение после восстановления здорово (`/api/health/ready` → `Healthy`),
      сосед на 8443 не задет. Попутно найдены и исправлены две ошибки самой инструкции:
      (1) команды восстановления обращались к томам `ezbook_api_*`, тогда как compose называет
      их по каталогу проекта — `app_api_*`; docker при неверном имени молча создаёт пустой том,
      то есть восстановление отчиталось бы об успехе, ничего не восстановив; (2) каталог
      `/var/backups/servicebooking` принадлежит root с правами 700, поэтому шаги §11.2 надо
      выполнять из-под `sudo -i`, иначе подстановка `$(ls -t ...)` не найдёт файлы.
      Прежний прогон 2026-09-09 был на локальном
      стенде. Повторить полный цикл (бэкап → `docker compose down -v` на тестовых данных →
      восстановление → проверка `GET /api/client-notes/photos/{id}`) здесь, с реальным
      `systemd`/`pg_dump`, дописать дату сюда и в §11.2.
- [x] **`fleetservice.service` на 8443 продолжает работать** после установки nginx и Docker.
      **2026-09-17, после полного разворачивания: ПРОШЛА.** `curl -k https://ezbook.ru:8443/`
      отвечает `302`, как и до начала работ. Docker, nginx и сертификат соседа не задели.
- [ ] **Деплой по кнопке из GitHub Actions, сквозь весь путь** (§9).
      **2026-09-17: прогон выполнен, пункты (а) и (б) закрыты, (в) остаётся.** `Deploy staging
      (develop)` отработал успешно; форс-команда проверена снаружи по отдельным ключам —
      ключ деплоя на произвольную команду отвечает `refused: command not in the allowlist`, на
      `health` отдаёт `{"status":"Healthy"}`, личный ключ к `ezbookdeploy` не пускает вовсе.
      Попутно найдено и исправлено: `ezbookdeploy` состоял в группе `sudo`, то есть имел
      `(ALL : ALL) ALL` — точечные `NOPASSWD` при этом были бессмысленны; и `ssh-copy-id`,
      выполненный по ошибке для `ezbookdeploy` вместо `server`, дописал туда личный ключ БЕЗ
      префикса `command=`. Обе дыры закрыты, повторно проверено.
      **Осталось (в):** при намеренно неверном подтверждающем input'е и при ref≠tag / ref≠master
      `deploy-production.yml` должен отказывать, не доходя до SSH. Проверить и дописать дату сюда.
- [x] **`curl -I https://ezbook.ru/embed/anything` не отдаёт `X-Frame-Options`/`frame-ancestors`**
      (Шаг 6.4). **2026-09-17, живой прод-стенд ezbook.ru: НЕ ПРОШЛА.** `/embed/test` отдавал
      `X-Frame-Options: DENY` и `frame-ancestors 'none'` — виджет не встраивался в чужой iframe
      вообще. Причина: `try_files ... /index.html;` в `location /embed/` — это внутренний
      редирект, и nginx пересопоставлял `/index.html` с `location /`, откуда и приезжали
      framing-заголовки; поскольку файла `/embed/<slug>` на диске никогда нет, в этот fallback
      уходил каждый запрос виджета без исключений. Исправлено в `deploy/nginx/ezbook.conf`
      (fallback теперь уходит в именованный `location @embed_fallback`, который nginx не
      пересопоставляет с `location /`). Проверено локально на `nginx/1.31.5` (brew): `/` —
      `X-Frame-Options`/CSP на месте, `/embed/test` — их нет, HSTS/nosniff/Referrer-Policy есть
      везде, `/api/...` не пострадал. **2026-09-17, повторная проверка на боевом стенде после
      применения фикса: ПРОШЛА.** `curl -I https://ezbook.ru/embed/test` — ни `X-Frame-Options`,
      ни `Content-Security-Policy`, при этом `Strict-Transport-Security`, `X-Content-Type-Options`
      и `Referrer-Policy` на месте; страница отдаётся (200, заголовок приложения). На `/`
      framing-заголовки по-прежнему строгие. Пункт закрыт.

---

## Почему так сделано

### Docker не из snap

Docker ставим только из официального репозитория Docker (docker-ce), не snap, не apt-пакет
`docker.io`, не `podman-docker`:

- snap-версия Docker работает в строгом confinement и не может делать bind-mount путей вне `$HOME`
  пользователя, от имени которого запущен snap — а этому стеку нужен bind-mount каталога
  `./legal` (правовые документы, §2.2) и доступ к путям вроде `/opt/ezbook` — снаружи `$HOME`
  снапа это тихо не сработает или сработает не так, как на любой другой машине, и это
  трудноуловимо на живом проде;
- `docker.io` из apt Ubuntu почти всегда более старая версия и не даёт `docker compose` (compose
  v2, плагин) без дополнительной возни;
- `podman-docker` — это alias на podman, у него другое поведение `compose` (эмуляция, не
  оригинальный движок), на этом стеке не проверялось никем.

certbot ставится через snap без этой проблемы — он ничего не bind-mount'ит в чужие каталоги
приложения, ему достаточно писать в свой собственный `/etc/letsencrypt` и один раз править конфиг
nginx через плагин.

### ufw включён и 443 нужно открыть явно

На машине `ufw` активен, политика по умолчанию — `deny` на входящие. Разрешены только `22`, `80`,
`8443`, `3389`, `3390` — настраивал кто-то осмысленно (`8443` соседа и RDP-порты открыты
намеренно). Порта 443 в списке нет.

Закрытый ufw порт снаружи неотличим от неверного проброса на роутере, от блокировки у провайдера
и от неподнятого сервиса — TCP-соединение принимается, данные не идут, таймаут. На поиск причины
в похожей ситуации ушло несколько часов; экономит диагностика по уровням:

1. `curl` на `127.0.0.1` с самой машины — петлевой интерфейс ufw не фильтрует, здесь ответит даже
   при закрытом порте.
2. Обращение к машине с другого устройства в той же локальной сети.
3. Обращение снаружи.

Уровень, на котором обрывается, и есть виновник: если локально работает, а из своей же сети
нет — это почти всегда файрвол хоста, а не сеть.

При изменении списка портов проверяйте `sudo ufw status verbose` до и после, и не включайте `ufw
default deny` без сверки каждого нужного порта — правило, отрезавшее 22, оставит вас без доступа
к машине, стоящей в другом помещении.

### Порт 80 нужен и после выпуска сертификата

Certbot получает сертификат через HTTP-01 — ходит на `ezbook.ru:80` снаружи, и именно
проброшенный и проверенный 80-й порт делает этот шаг рабочим без DNS-01/ручных TXT-записей.
Автопродление (snap-таймер, §6.3) использует тот же механизм HTTP-01 на том же 80-м порту — если
порт 80 закрыть или снять проброс после первого выпуска сертификата (например, «раз сертификат
уже есть, 80 больше не нужен»), ближайшее автопродление тихо не пройдёт, и сертификат истечёт без
предупреждения до следующей ручной проверки.

### Бэкап только локальный

Бэкап хранится локально, на той же машине, что и оригинальные данные. Внешней копии нет — решение
заказчика (SPEC R13): готового и бесплатного места для offsite-копии на момент цикла не нашлось, а
откладывать бэкап целиком до появления такого места хуже, чем сделать локальный сейчас.

Это защищает от порчи данных (кривая миграция, ошибка в коде, случайный `DELETE`) и от ошибки
оператора (кто-то удалил не то через админку).

Это **не защищает** от потери или недоступности самой машины (сгорел дата-центр, физически умер
диск, машину украли) — копия пропадёт вместе с оригиналом.

Когда появится место под внешнюю копию — заполнить `upload_offsite()` в
`deploy/backup/backup.sh` (сейчас осознанная заглушка-стаб именно под эту доработку) одной
функцией, без переписывания остального скрипта.

### `|| true` в `backup.sh` — несущий, а не защитный шум

Скрипт бэкапа работает под `set -euo pipefail`. Два места, где стоит `|| true`, не защита «на
всякий случай», а обязательное условие, без которого скрипт либо молча падает, либо рапортует о
несуществующей проблеме:

- при подсчёте размера последнего набора бэкапов: если файлов по шаблону ещё нет (самый первый
  запуск на машине), `ls` возвращает ненулевой код, `pipefail` протаскивает его в присваивание
  переменной — без `|| true` скрипт аварийно завершится до `fail()`, то есть без строки в логе и
  без алерта в GlitchTip, притом что для этого случая ниже уже есть отдельный фолбэк
  (`required_kb` = 100 МБ);
- при ротации старых бэкапов: шаблон еженедельных копий не совпадает ни с одним файлом до первого
  воскресного прогона — без `|| true` это будничным днём остановит скрипт ПОСЛЕ того, как бэкап
  уже успешно записан, но ДО финальной строки лога «всё ок», и systemd отрапортует юнит как
  упавший для бэкапа, который на самом деле состоялся.

### Пользователь `ezbookdeploy` — не root

Ключ от деплоя лежит на серверах GitHub (пусть и как зашифрованный секрет) — категория риска,
которой не было, пока деплоили только руками со своей машины. Если этот ключ когда-нибудь утечёт,
он не должен давать root, не должен давать интерактивный шелл и не должен давать доступ ни к чему,
кроме операций деплоя. Три независимых рубежа, ни один не полагается на остальные:

1. Отдельный системный пользователь `ezbookdeploy`, не тот, под которым вы сами ходите по SSH, и
   не `root`.
2. `command=` в `authorized_keys` — ключ физически не может запустить ничего, кроме одного
   форс-команд скрипта (`deploy/ssh-deploy-wrapper.sh`), который сам разбирает только узкий
   allowlist операций (`upload-release`, `deploy`, `rollback`, `health`) и отказывает во всём
   остальном.
3. Точечный `sudo` — даже внутри allowlist'а у пользователя нет `sudo` ни на что, кроме `nginx -t`
   и `systemctl reload nginx` — ровно то, что реально вызывают `deploy-remote.sh`/`rollback.sh`.

**Остаточный риск, принят осознанно.** Приватный ключ от машины, где лежат персональные данные
клиентов салонов (телефоны, фото к заметкам, Postgres), теперь хранится у стороннего сервиса
(GitHub Actions secrets), а не только у вас на диске. Ограничения выше сильно сужают, что можно
сделать этим конкретным ключом, если он утечёт — но не сводят риск к нулю: у GitHub бывают
инциденты безопасности, и любой человек с правом запускать `workflow_dispatch` в этом репозитории
фактически получает возможность запустить деплой (для прод-цели дополнительно требуется reviewer,
§9.2, но не для стенда). Это осознанный компромисс за «нажал кнопку — обновилось» без self-hosted
раннера на машине заказчика. Если позже понадобится сузить ещё сильнее — следующий шаг обычно
IP-allowlist на sshd для диапазонов GitHub Actions runners, либо переход на self-hosted раннер
прямо на этой машине.

### Что защищает `fleetservice.service` от `ezbookdeploy`

Ничего специально «запрещающего» заводить не нужно — достаточно того, чего НЕТ: `ezbookdeploy` не
в sudoers ни на один `systemctl`-юнит, кроме `reload nginx`, не входит ни в одну группу, под
которой запущен `fleetservice.service`, и форс-команд скрипт физически не содержит никакого
случая, упоминающего этот юнит или порт 8443 — allowlist в `deploy/ssh-deploy-wrapper.sh`
закрытый (`case ... *) refuse ...`), а не «разрешить всё, кроме». Изоляция — следствие того, что
`ezbookdeploy` в принципе не имеет прав ни на что за пределами перечисленного, а не отдельного
правила про этот конкретный сервис.

### Старт с черновой редакцией правовых текстов

Заводское состояние репозитория — черновик (`legal.json` с `version` вида `2026-09-08-draft`,
`isDraft: true`). Запускаться в production с этим состоянием разрешено осознанно — решение
заказчика, fail-fast на черновик намеренно не добавлен. Каждый регистрирующийся пользователь при
этом получает запись согласия именно с этой (черновой) версией. Если правовая вычитка уже прошла
до момента запуска — обновите текст и `legal.json` по §2.2 до первых регистраций, иначе более
ранние пользователи будут числиться согласившимися с черновиком, а не с финальным текстом.

### Storage root

Публичный каталог по умолчанию раздаётся из `wwwroot/uploads`; переменная `Storage__PublicRoot`
позволяет его переопределить, но ни одна из закоммитированных конфигураций её не задаёт — это
точка расширения на случай отдельного volume/диска под публичные файлы. Если всё же зададите её,
она не должна совпадать с корнем приложения (`/app` в контейнере) или лежать выше него — иначе
`UseStaticFiles` начнёт отдавать по `/uploads/...` сам код приложения (DLL, `App_Data` целиком);
API откажется стартовать с понятной ошибкой, если это обнаружит. Приватный каталог
(`Storage__PrivateRoot`) — по той же логике: если случайно окажется внутри `wwwroot` или внутри
фактического публичного корня, API тоже откажется стартовать, а не тихо начнёт раздавать чужие
фото публично.

### CSP: два самых хрупких места

CSP обязательно включает `img-src ... blob:` — приватные фото к заметкам о клиентах грузятся на
фронте как `blob:` через `useAuthedImage` (заголовок `Authorization` браузер не приложит к
обычному `<img src>`, только blob или proxy вообще работают). Без `blob:` в CSP эти фото просто
не отобразятся, при этом никакой ошибки в консоли, кроме глухого CSP-варнинга, не будет.

CSP обязательно включает `style-src ... https://fonts.googleapis.com` и `font-src 'self'
https://fonts.gstatic.com` — `frontend/index.html` грузит шрифты Newsreader/Inter с Google Fonts.
Без этих двух источников браузер отказывается загружать таблицу стилей (`default-src 'self'`
откатом блокирует и её, и шрифты), приложение тихо остаётся на системном шрифте, в консоли —
пачка `Refused to load the stylesheet`. Проверено живьём (прод-сборка фронта за локальным nginx
с этим CSP, headless-браузер): без директив — CSP-ошибка и `Refused to load`; с ними — стили и
`.woff2` грузятся 200 OK.

Yandex SmartCaptcha не требует в CSP ничего, кроме `smartcaptcha.yandexcloud.net` — проверено
живьём рендером реального виджета: `mc.yandex.ru` и `yastatic.net`, которые виджет подгружает,
запрашиваются изнутри его собственного iframe под CSP-политикой самого Яндекса, а не как запросы
верхнего документа. Если Яндекс когда-нибудь изменит виджет так, что он начнёт делать top-level
запросы напрямую, это всплывёт как `Refused to connect`/`Refused to load` в консоли при следующей
живой проверке.

`X-Frame-Options: DENY` + `Content-Security-Policy: frame-ancestors 'none'` стоят только в
`location /` — кроме `/embed/*` (виджет записи), для которого встраивание в чужой iframe — весь
смысл существования страницы. Новый `location` в конфиге наследует поведение блока, в который
попадёт — но именно здесь есть тонкость, см. следующий пункт.

### Фикс `/embed/` fallback от 2026-09-17

Живая проверка на проде (Шаг 6.4 checklist §16) нашла реальный дефект: `location /embed/` был
написан как `try_files $uri $uri/ /index.html;`, и это ломало ровно то, для чего `/embed/`
существует. Причина в семантике nginx, которая на первый взгляд не очевидна: последний аргумент
`try_files` — это **внутренний редирект**, а не «отдать этот файл как есть». nginx заново
прогоняет получившийся URI (`/index.html`) через список всех `location`, и `/index.html` не
подпадает под префикс `/embed/`, поэтому попадает в `location /` — и получает ЕГО `add_header`
(`X-Frame-Options: DENY`, CSP с `frame-ancestors 'none'`). Так как для маршрутов виджета
(`/embed/<slug>`, клиентская SPA-маршрутизация) файла на диске никогда не бывает, в этот fallback
уходил буквально каждый запрос — не редкий край, а 100% трафика виджета.

Исправление — увести fallback в именованный `location`:

```
location /embed/ {
    try_files $uri $uri/ @embed_fallback;
}
location @embed_fallback {
    rewrite ^ /index.html break;
}
```

Именованные `location` — единственный вид `location`, который nginx **не** пересопоставляет по
префиксу: URI, отданный в `@embed_fallback`, уже никогда не попадёт в `location /`. `try_files`
по-прежнему сначала пытается отдать реальный файл/директорию под `/embed/`, если они есть, и
только потом уходит в fallback — поведение для настоящих файлов не изменилось.

Второй вариант, который тоже решает проблему (`rewrite ^ /index.html break;` прямо внутри
`location /embed/`, без `try_files`), был отклонён, потому что тогда пришлось бы отдельно, через
`if`, проверять наличие настоящего файла на диске — а `if` в `location` в nginx официально не
рекомендован (непредсказуемое поведение при сочетании с другими директивами). Именованный
`location` этой развилки не создаёт.

Ни `/embed/`, ни `@embed_fallback` не объявляют собственных `add_header`, поэтому оба, как и
раньше, наследуют HSTS/`nosniff`/`Referrer-Policy` с уровня `server` — framing-ограничения на них
по-прежнему не действуют, но остальная часть US-47 действует.

Проверено локально (`nginx/1.31.5` через brew, конфиг из `deploy/nginx/ezbook.conf` один в один,
только `root`/`listen` подставлены под тестовый стенд): на `/` `X-Frame-Options` и CSP с
`frame-ancestors 'none'` присутствуют вместе с HSTS/`nosniff`/`Referrer-Policy`; на `/embed/test`
`X-Frame-Options` и CSP отсутствуют полностью, а HSTS/`nosniff`/`Referrer-Policy` есть; на `/api/`
поведение не изменилось.

**Применение на боевой машине.** Конфиг там уже отличается от репозитория — `certbot --nginx`
дописал в него собственный `server`-блок на 443, редирект с 80 на 443 и `ssl_certificate`/
`ssl_certificate_key`/`include options-ssl-nginx.conf`/`ssl_dhparam`. Файл нельзя просто
перезаписать содержимым из репозитория — это снесёт то, что дописал certbot, и сайт перестанет
отвечать по HTTPS. Правильная последовательность:

1. `sudo cp /etc/nginx/sites-available/ezbook.conf /etc/nginx/sites-available/ezbook.conf.bak-$(date +%F)`
   — на всякий случай, прежде чем трогать боевой файл.
2. Открыть боевой `/etc/nginx/sites-available/ezbook.conf` и найти в нём блок `location /embed/`
   (он будет в обоих `server`-блоках — на 80, если certbot оставил там копию для нешифрованного
   доступа, и точно в блоке на 443, который реально обслуживает трафик). Заменить в каждом
   найденном месте старый `try_files $uri $uri/ /index.html;` на новую пару `location /embed/` +
   `location @embed_fallback` — текст один в один как в `deploy/nginx/ezbook.conf` этого репо,
   вставляется точечно, ssl-директивы и остальной certbot'овский server-блок не трогать.
3. `sudo nginx -t` — обязательно перед reload, чтобы не уронить прод синтаксической ошибкой.
4. `sudo systemctl reload nginx` (не `restart` — reload не рвёт уже открытые соединения).
5. Повторить живую проверку из Шага 6.4 (`curl -I https://ezbook.ru/embed/anything`) и отметить
   результат и дату в чек-листе §16.

### Какую ветку катим и почему

Ветками распоряжается devops-роль: `master` — прод (только мердж из `release-candidate`, с тегом
версии), `release-candidate` — предрелизная стадия (мердж `develop`, когда решено резать релиз),
`develop` — интеграционный ствол (только завершённые циклы, прошедшие ревью и приёмку QA),
`cycle/NN-<слаг>` — один цикл, ответвляется от `develop`.

`develop` на момент написания содержит циклы 1–3. `master` намеренно не двигался: релиза не было,
ничего нигде не выкатывалось, GlitchTip не поднимался, CSP на скачивании выгрузки живьём не
проверялся (см. CURRENT_STATE.md, блок P0). Пока это не сделано, двигать `master` значило бы
пометить как прод то, что ни разу не запускалось. `release-candidate` создаётся только в момент
решения о релизе.

Цель этой выкатки — первая живая staging-проверка: убедиться, что CSP/скачивание выгрузки,
GlitchTip, откат и восстановление из бэкапа реально работают на настоящем nginx/systemd/Docker, а
не только на локальном стенде. Только после того, как этот прогон пройдёт и по нему объявлено
решение о релизе, код едет дальше: `develop` → `release-candidate` → регрессия QA → `master` с
тегом. Не деплойте `master` на эту машину по привычке — на момент написания в нём нет циклов 1–3
вообще, это откат на десятки коммитов назад, а не «более стабильная» версия.

Конвенция именования цикл-веток — `cycle/NN-<слаг>`: двузначный порядковый номер цикла плюс
короткий слаг по сути задачи (например, `cycle/04-payments`). Номер, а не дата: датой git и так
располагает, а дата создания ветки внутри растянутого цикла выбирается произвольно и вводит в
заблуждение. Номер, а не буква: буквы заканчиваются на `Z`. Ведущий ноль обязателен — иначе
`cycle/10-…` встанет в алфавитном списке между первым и вторым циклом.

### Почему AlmaLinux-вариант не оставлен как второй параллельный

До этой правки раннбук был написан под AlmaLinux 8 (VPS reg.ru), но фактически ни разу не
разворачивался в бою — это была инструкция «на будущее», а не описание реально работающей машины.
Реальная целевая машина — Ubuntu 24.04, и это не два равнозначных варианта окружения, а замена
одного гипотетического окружения на одно реальное.

Держать оба варианта параллельно («если у вас AlmaLinux — читайте так, если Ubuntu — эдак») было
осознанно отклонено: два набора инструкций в одном файле неизбежно расходятся при следующих
правках — кто-то поправит команду для одной ОС и забудет про вторую, это доказанный источник
багов в документации, а не гипотетический риск. Если появится второй, тоже реальный целевой хост
на другом дистрибутиве — для него стоит завести отдельный файл (`DEPLOY-<дистрибутив>.md`, по
аналогии с уже существующим `DEPLOY-windows.md`), а не дописывать развилки внутрь этого.
