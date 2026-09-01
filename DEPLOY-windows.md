# Деплой ServiceBooking на Windows-ВМ (VK Cloud), домен ezbook.ru

Схема: PostgreSQL и backend — как Windows-службы (PostgreSQL ставится сервисом сразу
инсталлятором, backend оборачивается в службу через NSSM — без правки C#-кода). IIS
отдаёт статику фронтенда и проксирует `/api` и `/uploads` на backend через модули
URL Rewrite + Application Request Routing (ARR) — это Windows-аналог того, что на Linux
делал nginx. TLS — через win-acme (аналог certbot для IIS).

Все команды — в **PowerShell, запущенном от администратора**, через RDP на самой ВМ,
если не указано иное.

---

## 0. Перед началом

- В DNS домена `ezbook.ru` (панель reg.ru → DNS) A-записи `ezbook.ru` и `www.ezbook.ru`
  должны указывать на выделенный IP этой ВМ в VK Cloud. Проверить с любого компьютера:

  ```bash
  dig +short ezbook.ru @8.8.8.8
  dig +short www.ezbook.ru @8.8.8.8
  ```

- В консоли VK Cloud проверьте, что в **Security Group** (группе безопасности) ВМ открыты
  порты **80** и **443** на входящих соединениях — Windows Firewall (настроим ниже) не
  поможет, если трафик режется раньше, на уровне облака.

- Подключение по RDP с правами администратора должно уже работать.

---

## 1. Базовые компоненты

```powershell
# Git, Node.js, .NET 8 SDK — через winget (уже есть в Windows 10/11)
# Ставим именно SDK, а не только ASP.NET Core Runtime: `dotnet publish` собирает
# проект (MSBuild), а не просто запускает готовую сборку — SDK уже включает нужный
# рантайм, отдельно его ставить не нужно.
winget install --id Git.Git -e --silent
winget install --id OpenJS.NodeJS.LTS -e --silent
winget install --id Microsoft.DotNet.SDK.8 -e --silent

# Закройте это окно PowerShell и откройте новое (от имени администратора) —
# иначе PATH не подхватит новые программы в текущей сессии
```

```powershell
# По умолчанию PowerShell блокирует запуск скриптов — npm сам является .ps1-обёрткой,
# без этого `npm ci` / `npm run build` упадут с PSSecurityException
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine -Force
```

```powershell
# PostgreSQL — интерактивный инсталлятор (спросит пароль для пользователя postgres, запомните его)
winget install --id PostgreSQL.PostgreSQL.16 -e
```

```powershell
# IIS
$features = @(
  "IIS-WebServerRole","IIS-WebServer","IIS-CommonHttpFeatures","IIS-HttpErrors","IIS-ApplicationDevelopment",
  "IIS-NetFxExtensibility45","IIS-HealthAndDiagnostics","IIS-HttpLogging","IIS-Security","IIS-RequestFiltering",
  "IIS-Performance","IIS-WebServerManagementTools","IIS-ManagementConsole","IIS-StaticContent","IIS-DefaultDocument"
)
foreach ($f in $features) { Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart }
```

```powershell
# URL Rewrite и Application Request Routing — официальные модули Microsoft для IIS
Invoke-WebRequest -Uri "https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi" -OutFile "$env:TEMP\rewrite.msi"
Start-Process msiexec.exe -ArgumentList "/i `"$env:TEMP\rewrite.msi`" /quiet /norestart" -Wait

Invoke-WebRequest -Uri "https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi" -OutFile "$env:TEMP\arr.msi"
Start-Process msiexec.exe -ArgumentList "/i `"$env:TEMP\arr.msi`" /quiet /norestart" -Wait

# Включить ARR как reverse proxy (без этого URL Rewrite не сможет проксировать на localhost:5000)
& "$env:windir\System32\inetsrv\appcmd.exe" set config -section:system.webServer/proxy /enabled:"True" /commit:apphost
```

Если ссылки на .msi к моменту, когда вы это читаете, не отвечают — модули можно скачать
вручную с официальной страницы IIS: `https://www.iis.net/downloads/microsoft/url-rewrite`
и `https://www.iis.net/downloads/microsoft/application-request-routing`.

```powershell
# NSSM — обёртка процесса в Windows-службу (не требует прав, кроме админских при install)
New-Item -ItemType Directory -Force -Path C:\nssm | Out-Null
Invoke-WebRequest -Uri "https://nssm.cc/release/nssm-2.24.zip" -OutFile "$env:TEMP\nssm.zip"
Expand-Archive "$env:TEMP\nssm.zip" -DestinationPath "$env:TEMP\nssm" -Force
Copy-Item "$env:TEMP\nssm\nssm-2.24\win64\nssm.exe" "C:\nssm\nssm.exe" -Force
```

```powershell
# Firewall
New-NetFirewallRule -DisplayName "HTTP"  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow
New-NetFirewallRule -DisplayName "HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

**win-acme** (клиент Let's Encrypt для IIS) ставится позже, в разделе 7 — устанавливать
его до появления сайта в IIS смысла нет.

---

## 2. Клонирование репозитория

```powershell
New-Item -ItemType Directory -Force -Path C:\ezbook | Out-Null
cd C:\ezbook
git clone https://github.com/Douk771/ServiceBooking.git app
cd app
git checkout feature/deployfirst
```

(`feature/deployfirst` — рабочая ветка с редизайном, деплой-тулингом и последними правками;
`master` их ещё не содержит.)

---

## 3. База данных

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -c "CREATE DATABASE servicebooking;"
```

Спросит пароль пользователя `postgres`, заданный на шаге установки.

---

## 4. Backend

```powershell
cd C:\ezbook\app\ServiceBooking.API
dotnet publish -c Release -o C:\ezbook\publish\api
```

Секреты — из шаблона в исходниках, но кладём файл **в папку публикации**, не в репозиторий:

```powershell
Copy-Item C:\ezbook\app\ServiceBooking.API\appsettings.Production.json.example `
          C:\ezbook\publish\api\appsettings.Production.json
notepad C:\ezbook\publish\api\appsettings.Production.json
```

Заполните в открывшемся файле:
- `ConnectionStrings:DefaultConnection` — пароль от `postgres` из шага 3
- `Jwt:Key` — длинная случайная строка (сгенерировать: `[Convert]::ToBase64String((1..48|%{Get-Random -Max 256}))`)
- `SuperAdmin:Phone` — ваш номер, станет суперадмином при первом запуске
- `SuperAdmin:Password` — пароль для входа под этим номером (мин. 8 символов, минимум
  одна цифра, одна строчная и одна заглавная буква — это требования Identity в этом
  проекте). **Оба поля, `Phone` и `Password`, обязательны** — аккаунт создаётся при
  первом старте, только если заполнены сразу оба; если аккаунт с этим номером уже
  зарегистрирован вручную через сайт, автосоздание его не тронет и роль SuperAdmin
  само по себе не добавит.
- `SmartCaptcha:SecretKey` / `SiteKey` — из кабинета Yandex Cloud SmartCaptcha

Сохраните и закройте notepad, затем регистрируем и запускаем службу:

```powershell
New-Item -ItemType Directory -Force -Path C:\ezbook\logs | Out-Null

C:\nssm\nssm.exe install ServiceBookingApi "C:\Program Files\dotnet\dotnet.exe" "C:\ezbook\publish\api\ServiceBooking.API.dll"
C:\nssm\nssm.exe set ServiceBookingApi AppDirectory "C:\ezbook\publish\api"
C:\nssm\nssm.exe set ServiceBookingApi AppEnvironmentExtra ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://localhost:5000
C:\nssm\nssm.exe set ServiceBookingApi AppStdout C:\ezbook\logs\api-stdout.log
C:\nssm\nssm.exe set ServiceBookingApi AppStderr C:\ezbook\logs\api-stderr.log
C:\nssm\nssm.exe set ServiceBookingApi Start SERVICE_AUTO_START

Start-Service ServiceBookingApi
Get-Service ServiceBookingApi   # должен быть Status: Running
```

Проверка:

```powershell
Invoke-WebRequest http://localhost:5000/api/companies -UseBasicParsing | Select-Object StatusCode
```

Ожидаем `200`. Если нет — смотрим `C:\ezbook\logs\api-stderr.log` и присылаете мне содержимое.

---

## 5. Фронтенд

```powershell
cd C:\ezbook\app\frontend

# Ключ капчи подставляется во время сборки (Vite инлайнит переменные в билд)
"VITE_SMARTCAPTCHA_SITEKEY=<ВАШ_SITE_KEY>" | Out-File -Encoding utf8 .env.production

npm ci
npm run build

New-Item -ItemType Directory -Force -Path C:\inetpub\wwwroot\ezbook | Out-Null
Copy-Item -Recurse -Force dist\* C:\inetpub\wwwroot\ezbook\
```

`web.config` с правилами проксирования `/api` и `/uploads` на backend уже лежит в
`frontend/public/web.config` — Vite сам копирует его в `dist/` при каждой сборке,
руками ничего донастраивать не нужно.

---

## 6. Сайт в IIS

Сначала — общая часть, одинаковая в обоих случаях:

```powershell
Import-Module WebAdministration

# Default Web Site по умолчанию слушает порт 80 — выключаем, иначе конфликт портов
Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
Set-ItemProperty "IIS:\Sites\Default Web Site" -Name serverAutoStart -Value False -ErrorAction SilentlyContinue

New-Website -Name "ezbook" -PhysicalPath "C:\inetpub\wwwroot\ezbook" -Port 80 -HostHeader "ezbook.ru"
New-WebBinding -Name "ezbook" -Protocol http -Port 80 -HostHeader "www.ezbook.ru"
```

Биндинги сделаны через `HostHeader` — сайт откликается только на запросы с заголовком
`Host: ezbook.ru` (или `www.ezbook.ru`), а не на любой запрос к этому IP. Дальше — как
проверить сайт, в зависимости от того, готов ли уже DNS.

### Вариант А — A-записи `ezbook.ru`/`www.ezbook.ru` уже указывают на IP этой ВМ

Проверить с любого компьютера: `dig +short ezbook.ru @8.8.8.8` должен вернуть IP ВМ.

Если да — просто откройте `http://ezbook.ru` в браузере, должна открыться главная
страница. Ничего дополнительно настраивать не нужно, переходите к разделу 7 (HTTPS).

### Вариант Б — DNS ещё не настроен (или настроен, но не успел распространиться)

`HostHeader`-биндинг не даст зайти на сайт по голому IP — заголовок `Host` не совпадёт.
Два способа проверить, не дожидаясь DNS — выбирайте по удобству.

**Способ 1 — открыть сайт по IP с любого устройства (проще всего, работает и с мобилки).**
Добавьте на ВМ ещё один биндинг без ограничения по `HostHeader` — тогда IIS откликается
на запрос с любым Host-заголовком, в том числе на голый IP:

```powershell
New-WebBinding -Name "ezbook" -Protocol http -Port 80
```

Теперь `http://<IP_ВМ>` открывается откуда угодно — с ноутбука, с телефона, из любой
сети, без правки чего-либо на стороне клиента. Фронтенд ходит в API относительными
путями (`/api/...`), так что по IP всё работает так же, как будет работать по домену.

Когда DNS настроится — уберите этот биндинг, иначе сайт продолжит откликаться на любой
домен, который кто-нибудь ещё направит на этот IP:

```powershell
Remove-WebBinding -Name "ezbook" -Protocol http -Port 80 -HostHeader ""
```

**Способ 2 — через `hosts`-файл, если хочется именно проверить домен `ezbook.ru`.**
Работает только на том конкретном компьютере, где правите файл (на телефоне неудобно —
там правка `hosts` требует root/jailbreak). На **компьютере, с которого браузите** (не
на самой ВМ!):

- Windows: `C:\Windows\System32\drivers\etc\hosts`
- macOS/Linux: `/etc/hosts`

Добавьте туда (нужны права администратора на редактирование файла):

```
<IP_ВМ>    ezbook.ru
<IP_ВМ>    www.ezbook.ru
```

Теперь браузер отправит правильный заголовок `Host: ezbook.ru`, а соединение уйдёт на
реальный IP ВМ. Откройте `http://ezbook.ru` — должна открыться главная страница.

После того как пропишете настоящие A-записи в панели reg.ru, эти строчки из `hosts` можно
убрать (они нужны были только для теста в обход DNS).

**Важно:** оба способа — только для вашего собственного теста в браузере/с телефона.
Раздел 7 (HTTPS через win-acme) они не заменяют — Let's Encrypt проверяет домен со своих
серверов через настоящий интернет, а не с вашего устройства, так что настоящие A-записи
на IP этой ВМ всё равно понадобятся до выпуска сертификата.

---

## 7. HTTPS через win-acme

```powershell
New-Item -ItemType Directory -Force -Path C:\win-acme | Out-Null
```

Скачайте актуальный релиз (файл вида `win-acme.v2.х.х.х.х64.pluggable.zip`) вручную со
страницы `https://github.com/win-acme/win-acme/releases` (ссылки на конкретную версию
быстро устаревают, поэтому не даю прямую ссылку) и распакуйте содержимое zip в `C:\win-acme`.

```powershell
cd C:\win-acme
.\wacs.exe
```

Дальше — интерактивный мастер:
1. `N` — создать новый сертификат (простой вариант, "default settings").
2. Мастер найдёт сайт `ezbook` в IIS и предложит его — подтвердите, включив оба
   host header (`ezbook.ru` и `www.ezbook.ru`).
3. Укажите email для уведомлений об истечении сертификата.
4. Мастер сам добавит HTTPS-биндинг с сертификатом в IIS и создаст задачу в
   Планировщике заданий Windows для автопродления — руками ничего донастраивать не надо.

---

## 8. Проверка

Откройте `https://ezbook.ru` — должна открыться главная страница по HTTPS.
Зарегистрируйте тестовый аккаунт, войдите под номером из `SuperAdmin:Phone` —
должна появиться ссылка «Админ» в шапке.

---

## 9. Обновление после будущих изменений в коде

```powershell
cd C:\ezbook\app
git pull

Stop-Service ServiceBookingApi
dotnet publish ServiceBooking.API -c Release -o C:\ezbook\publish\api
Start-Service ServiceBookingApi

cd frontend
npm ci
npm run build
Copy-Item -Recurse -Force dist\* C:\inetpub\wwwroot\ezbook\
```

(Службу backend'а обязательно останавливаем перед `dotnet publish` — иначе publish не
сможет перезаписать `ServiceBooking.API.dll`, пока файл занят работающим процессом.)

Кнопки в духе Rider-варианта для Linux здесь пока нет — доступ к ВМ только по RDP, без
SSH/WinRM. Если понадобится «одна кнопка» и отсюда — можно включить PowerShell Remoting
(WinRM) на ВМ и дергать этот же скрипт из Rider через `Invoke-Command`; отдельно скажите,
если это нужно, распишу настройку.

---

## Если что-то пошло не так

Пришлите мне вывод одной из этих команд — разберём по выводу:

```powershell
Get-Content C:\ezbook\logs\api-stderr.log -Tail 100
Get-Service ServiceBookingApi
Test-NetConnection localhost -Port 5000
Get-Website
& "$env:windir\System32\inetsrv\appcmd.exe" list config -section:system.webServer/proxy
```
