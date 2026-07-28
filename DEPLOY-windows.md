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
# Git, Node.js, .NET 8 Runtime — через winget (уже есть в Windows 10/11)
winget install --id Git.Git -e --silent
winget install --id OpenJS.NodeJS.LTS -e --silent
winget install --id Microsoft.DotNet.AspNetCore.8 -e --silent

# Перезапустите PowerShell после этого блока, чтобы PATH подхватил новые программы
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
git clone <URL_ВАШЕГО_РЕПОЗИТОРИЯ> app
cd app
```

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
Invoke-WebRequest http://localhost:5000/swagger/index.html -UseBasicParsing | Select-Object StatusCode
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

```powershell
Import-Module WebAdministration

# Default Web Site по умолчанию слушает порт 80 — выключаем, иначе конфликт портов
Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
Set-ItemProperty "IIS:\Sites\Default Web Site" -Name serverAutoStart -Value False -ErrorAction SilentlyContinue

New-Website -Name "ezbook" -PhysicalPath "C:\inetpub\wwwroot\ezbook" -Port 80 -HostHeader "ezbook.ru"
New-WebBinding -Name "ezbook" -Protocol http -Port 80 -HostHeader "www.ezbook.ru"
```

Проверка (пока без HTTPS): откройте `http://ezbook.ru` в браузере — должна открыться
главная страница сайта.

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
