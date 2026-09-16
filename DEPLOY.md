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
docker run --rm -v ezbook_api_uploads:/from -v ezbook_api_private_uploads:/from-private \
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
mkdir -p /var/www/ezbook/releases
```

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
проверьте живьём после выпуска сертификата:

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
docker run --rm -v ezbook_api_uploads:/dst -v /var/backups/servicebooking:/src alpine \
  sh -c "rm -rf /dst/* && cd /dst && tar xzf /src/$(basename "$LATEST_UPLOADS")"
docker run --rm -v ezbook_api_private_uploads:/dst -v /var/backups/servicebooking:/src alpine \
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

**Шаг 12.1.** Поднимите GlitchTip:

```bash
cd /opt/ezbook/app
docker compose -f docker-compose.glitchtip.yml --env-file .env up -d
```

Первый вход — по адресу из `docker-compose.glitchtip.yml` (GlitchTip не публикуется на голом
порту без защиты входа, см. комментарий в файле). Создайте организацию и проект (тип
Node/Other — нужен только DSN). Скопируйте DSN проекта: Settings → Client Keys (DSN).

**Шаг 12.2.** Подключите приложение:

```bash
# в .env на машине
SENTRY_DSN=https://<key>@<ваш-домен-glitchtip>/1
```

```bash
docker compose -f docker-compose.prod.yml --env-file .env up -d api   # применить переменную
```

Пустое значение — приложение работает без трекера, это нормальное состояние до тех пор, пока
GlitchTip не поднят. `backup.sh` использует тот же `SENTRY_DSN` для алерта о неуспешном бэкапе —
заполнили один раз, получили оба канала алертов.

**Шаг 12.3.** Проверка: спровоцируйте тестовую ошибку (или дождитесь органической 500) — в
GlitchTip должно появиться ровно одно новое issue. Ретенция событий — 30 дней
(`GLITCHTIP_MAX_EVENT_LIFE_DAYS` в `docker-compose.glitchtip.yml`).

**Шаг 12.4.** Настройте уведомления: `Settings → Notifications`, e-mail заказчика. Что вызывает
алерт (и только это — остальное тихое):

| Повод | Откуда |
|---|---|
| Новая, ранее не встречавшаяся ошибка | GlitchTip «new issue», автоматически |
| Всплеск известной ошибки | Правило в `Settings → Alerts` — настройте под свою нагрузку |
| Неуспешный прогон бэкапа | `backup.sh` шлёт событие сам (§11.1 п. 6) |
| `/api/health/ready` не отвечает 3 проверки подряд | `deploy/monitor/health-alert.sh`, §12.5 |

Дневная сводка — встроенный digest GlitchTip (`Settings → Notifications → Daily digest`).

Если вся машина недоступна — GlitchTip недоступен вместе с ней, алерта не будет: мониторинг
снаружи машины в объём этого раннбука не входит.

**Шаг 12.5.** Поднимите монитор доступности (алерт на долгий 503):

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

- [ ] **Скачивание выгрузки данных под новым CSP (`blob:` + `<a download>`), в браузере, с
      открытой консолью.** Откройте DevTools → Console и Network до клика, скачайте выгрузку,
      убедитесь, что файл действительно скачался и в консоли нет `Refused to...` по CSP.
- [ ] **GlitchTip end-to-end** (§12): поднять на этой машине, получить DSN, прописать в `.env`,
      спровоцировать тестовую ошибку, убедиться, что она дошла до GlitchTip.
- [ ] **Откат одной командой** (`deploy/rollback.sh`, §10.2) на реальном nginx и systemd этой
      машины — прогон 2026-09-09 был на локальном стенде. Сделать два последовательных деплоя,
      затем `rollback.sh` без аргументов, убедиться, что `readiness` подтверждается после
      отката, дописать дату сюда и в §10.2.
- [ ] **Восстановление из бэкапа** (§11.2) на этой машине — прогон 2026-09-09 был на локальном
      стенде. Повторить полный цикл (бэкап → `docker compose down -v` на тестовых данных →
      восстановление → проверка `GET /api/client-notes/photos/{id}`) здесь, с реальным
      `systemd`/`pg_dump`, дописать дату сюда и в §11.2.
- [ ] **`fleetservice.service` на 8443 продолжает работать** после установки nginx и Docker.
      Проверить сразу после §1 и ещё раз после полного разворачивания — дёрнуть его так же, как
      дёргали до начала работ, и сравнить с поведением до.
- [ ] **Деплой по кнопке из GitHub Actions, сквозь весь путь** (§9) — ни разу не прогонялся
      живьём. Сделать один прогон `Deploy staging (develop)` и убедиться, что: (а) форс-команда
      `deploy/ssh-deploy-wrapper.sh` действительно ставится и отрабатывает, (б) `sudo -u
      ezbookdeploy sudo -n systemctl status fleetservice.service` отказывает, как задумано, (в)
      при намеренно неверном подтверждающем input'е и при ref≠tag/ref≠master
      `deploy-production.yml` действительно отказывает, не доходя до SSH. Дописать дату и
      результат сюда.

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
попадёт.

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
