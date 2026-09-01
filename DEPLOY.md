# Деплой ServiceBooking на VPS (reg.ru), домен ezbook.ru

Схема: Postgres + ASP.NET API в Docker (только на 127.0.0.1:5000, наружу не торчат),
фронтенд собирается статикой и отдаётся хостовым nginx, nginx же терминирует HTTPS
(Let's Encrypt) и проксирует `/api` и `/uploads` на контейнер API.

Все команды ниже выполняются по SSH на самом VPS, если не указано иное.

---

## 0. Перед началом

- В DNS домена `ezbook.ru` (в панели reg.ru → раздел "Домены" → управление DNS) должны
  стоять A-записи `ezbook.ru` и `www.ezbook.ru`, указывающие на IP вашего VPS.
  Проверить с любого компьютера:

  ```bash
  dig +short ezbook.ru
  dig +short www.ezbook.ru
  ```

  Оба должны вернуть IP сервера. Если ещё не настроено — до этого TLS-сертификат не выдастся.

- Узнайте IP и способ входа по SSH (обычно `ssh root@ВАШ_IP`, реже отдельный пользователь —
  reg.ru присылает это на почту при заказе VPS).

---

## 1. Первичная настройка сервера

Сервер на **AlmaLinux 8** — пакеты через `dnf`, файрвол через `firewalld`,
SELinux по умолчанию в режиме `enforcing` (это не мешает, но нужно явно
разрешить nginx проксировать запросы на backend — см. ниже).

```bash
ssh root@ВАШ_IP

dnf update -y

# Docker (официальный репозиторий для RHEL-семейства — подходит и для AlmaLinux)
dnf install -y dnf-plugins-core
dnf config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
dnf install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker

# nginx
dnf install -y nginx
systemctl enable --now nginx

# certbot (из EPEL)
dnf install -y epel-release
dnf install -y certbot python3-certbot-nginx

# прочее
dnf install -y git nano

# Firewall — открываем только SSH, HTTP, HTTPS
systemctl enable --now firewalld
firewall-cmd --permanent --add-service=ssh
firewall-cmd --permanent --add-service=http
firewall-cmd --permanent --add-service=https
firewall-cmd --reload

# SELinux: разрешить nginx проксировать запросы на localhost:5000 (backend в Docker)
setsebool -P httpd_can_network_connect 1
```

Проверка: `docker --version`, `nginx -v` — обе команды должны отработать без ошибок.
`getenforce` должен вывести `Enforcing` — это нормально, всё уже разрешено выше.

---

## 2. Клонирование репозитория

```bash
mkdir -p /opt/ezbook
cd /opt/ezbook
git clone https://github.com/Douk771/ServiceBooking.git app
cd app
git checkout feature/deployfirst
```

(`feature/deployfirst` — рабочая ветка с редизайном, деплой-тулингом и последними правками;
`master` их ещё не содержит.)

Если репозиторий приватный — заведите на GitHub/GitLab deploy-key или используйте
HTTPS-токен; это разовая настройка доступа, которую тоже нужно сделать вам самим.

---

## 3. Секреты (.env)

```bash
cp .env.production.example .env
nano .env
```

Заполните:
- `POSTGRES_PASSWORD` — сгенерируйте: `openssl rand -base64 24`
- `JWT_KEY` — сгенерируйте: `openssl rand -base64 48` (**обязательно**: минимум 32 символа и не равно
  плейсхолдеру из `appsettings.json` — иначе контейнер откажется стартовать, см. ниже)
- `SUPERADMIN_PHONE` — ваш номер в формате `+7XXXXXXXXXX`, станет суперадмином при первом запуске
- `SUPERADMIN_PASSWORD` — пароль для аккаунта суперадмина (**обязательно**: не равно плейсхолдеру
  `Admin12345` — иначе контейнер откажется стартовать)
- `SMARTCAPTCHA_SECRET_KEY` / `SMARTCAPTCHA_SITE_KEY` — из кабинета Yandex Cloud SmartCaptcha

Сохраните (Ctrl+O, Enter, Ctrl+X в nano). Файл `.env` не попадёт в git — он в `.gitignore`.

**Перед запуском проверьте подстановку переменных** (не поднимает контейнеры, только печатает
итоговый конфиг):

```bash
docker compose -f docker-compose.prod.yml --env-file .env config
```

Убедитесь, что `Jwt__Key`/`SuperAdmin__Password` в выводе — реальные значения, а не пустые строки.
С этого цикла API **фейлится при старте** (fail-fast, не поднимается вовсе), если `Jwt:Key` или
`SuperAdmin:Password` отсутствуют либо остались плейсхолдером — так безопаснее, чем боевой контейнер,
который тихо стартовал бы со значением `Admin12345`/недлинным ключом из `appsettings.json`.

---

## 4. Backend + Postgres в Docker

```bash
cd /opt/ezbook/app
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

Проверка, что всё поднялось:

```bash
docker compose -f docker-compose.prod.yml ps
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5000/api/companies   # ожидаем 200
```

Swagger в Production **не поднимается вовсе** (доступен только при локальной разработке,
`ASPNETCORE_ENVIRONMENT=Development`) — `curl .../swagger/index.html` теперь корректно вернёт `404`,
это не признак проблемы.

Если `curl` не вернул 200 — посмотрите логи и пришлите мне вывод:

```bash
docker compose -f docker-compose.prod.yml logs api --tail=100
```

---

## 5. Сборка фронтенда

```bash
# Node.js (если ещё не стоит)
curl -fsSL https://rpm.nodesource.com/setup_20.x | bash -
dnf install -y nodejs

cd /opt/ezbook/app/frontend

# Ключ капчи должен попасть в сборку на этапе билда (Vite подставляет переменные во время build)
echo "VITE_SMARTCAPTCHA_SITEKEY=<ВАШ_SITE_KEY>" > .env.production

npm ci
npm run build

mkdir -p /var/www/ezbook
cp -r dist /var/www/ezbook/
restorecon -Rv /var/www/ezbook   # поправить SELinux-контекст файлов, чтобы nginx мог их читать
```

Проверка: `ls /var/www/ezbook/dist/index.html` — файл должен существовать.

---

## 6. nginx + HTTPS

На AlmaLinux (в отличие от Ubuntu) конфиги сайтов кладутся прямо в `/etc/nginx/conf.d/` —
папка уже подключена через `include` в `/etc/nginx/nginx.conf`, отдельно её включать не нужно.

```bash
cp /opt/ezbook/app/deploy/nginx/ezbook.conf /etc/nginx/conf.d/ezbook.conf

nginx -t        # проверка синтаксиса — должно быть "syntax is ok" / "test is successful"
systemctl reload nginx

# Выпуск и установка сертификата (certbot сам допишет server-блок на 443 и настроит редирект с 80)
certbot --nginx -d ezbook.ru -d www.ezbook.ru
```

Certbot спросит email (для уведомлений об истечении сертификата) и согласие с условиями —
ответьте на вопросы в терминале. Автопродление сертификата на AlmaLinux настраивается
отдельно (пакет certbot из EPEL не ставит systemd-таймер сам) — добавьте его вручную:

```bash
echo "0 3 * * * root certbot renew --quiet --deploy-hook 'systemctl reload nginx'" \
  > /etc/cron.d/certbot-renew
```

Проверить, что обновление сработает, можно командой `certbot renew --dry-run`.

---

## 7. Проверка

Откройте `https://ezbook.ru` в браузере — должна открыться главная страница.
Зарегистрируйте тестовый аккаунт, проверьте вход под номером из `SUPERADMIN_PHONE` —
у него должна появиться ссылка «Админ» в шапке.

---

## 8. Обновление после будущих изменений в коде

Та же последовательность теперь есть скриптом на сервере — `deploy/deploy-remote.sh`
(попадёт туда после первого `git pull`). Вручную его можно прогнать так:

```bash
cd /opt/ezbook/app
git pull
bash deploy/deploy-remote.sh
```

Но проще — деплоить одной кнопкой с локальной машины, см. следующий раздел.

---

## 9. Деплой по кнопке (из Rider)

Идея: локальный скрипт `deploy/deploy.sh` подключается к VPS по SSH и запускает там
`git pull && deploy/deploy-remote.sh`. Чтобы Rider мог просто нажать ▶ без ввода пароля
на каждый клик, нужен вход по SSH-ключу.

### 9.1. Настройка SSH-ключа (один раз, на вашей локальной машине)

```bash
# Если ключа ещё нет
ssh-keygen -t ed25519 -C "deploy-ezbook"

# Скопировать публичный ключ на сервер
ssh-copy-id root@31.31.197.39
```

Проверка — эта команда должна отработать **без запроса пароля**:

```bash
ssh root@31.31.197.39 "echo ok"
```

Если просит пароль — ключ не скопировался или на сервере отключён вход по ключу
(`PubkeyAuthentication` в `/etc/ssh/sshd_config`), разберём по выводу `ssh -v`.

### 9.2. Локальный конфиг деплоя

```bash
cp .deploy.env.example .deploy.env
```

В `.deploy.env` уже стоит `SSH_HOST=root@31.31.197.39` — поменяйте, если IP/пользователь
другие. Файл в `.gitignore`, в репозиторий не попадёт.

Проверка вручную (без Rider):

```bash
./deploy/deploy.sh
```

Если отработало без ошибок — на сервере обновился и backend, и frontend.

### 9.3. Кнопка в Rider

1. **Run → Edit Configurations… → + → Shell Script**
2. Заполните:
   - **Name**: `Deploy to VPS`
   - **Script path**: `deploy/deploy.sh` (или полный путь до него)
   - **Working directory**: корень проекта (`ServiceBooking`)
3. **OK** — сохранить.
4. Конфигурация появится в выпадающем списке рядом с зелёной кнопкой ▶ в тулбаре
   Rider. Выберите её и нажмите ▶ (или Shift+F10) — это и есть «кнопка деплоя»:
   локально `git pull`-ом не нужно, скрипт сам подключится к серверу, обновит код,
   пересоберёт backend-контейнер и фронтенд, перезагрузит nginx.

Вывод скрипта будет в панели **Run** внизу Rider — так же, как у обычного запуска
приложения, только выполняется он на сервере.

---

## Если что-то пошло не так

Пришлите мне вывод одной из этих команд — разберём по выводу:

```bash
docker compose -f docker-compose.prod.yml logs api --tail=100
nginx -t
journalctl -u nginx --no-pager --tail=50
certbot certificates

# Если nginx отдаёт 502/403 без явной причины в логах — часто виноват SELinux, проверьте:
ausearch -m avc -ts recent
```
