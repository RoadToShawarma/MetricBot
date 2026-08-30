# MetricBot

[Русский](#русский) · [English](#english)

## Русский

MetricBot — настольное приложение для Windows, которое автоматически посещает заданные веб-страницы через настраиваемые интервалы. Программа работает в фоновом режиме, поддерживает страницы с JavaScript и показывает историю работы в журнале.

> Используйте MetricBot только для сайтов, которыми вы владеете или на посещение которых у вас есть разрешение. Соблюдайте правила сайтов и применимое законодательство.

### Возможности

- посещение списка URL через установленный Google Chrome или Chromium;
- автоматическая установка совместимой версии Chromium из настроек;
- резервный режим через `HttpClient`, если браузер недоступен (без выполнения JavaScript);
- три режима обхода: все ссылки, по одной по очереди или случайная ссылка;
- случайный интервал между обходами в заданном диапазоне;
- импорт и экспорт списка URL в TXT-файл;
- запуск вместе с Windows, запуск свёрнутым и автоматическое начало обхода;
- работа из системного трея и управление запуском без открытия главного окна;
- журнал событий в интерфейсе и файле;
- парольная защита управления приложением;
- защита от запуска нескольких экземпляров программы.

### Совместимость

| Выпуск | Целевая платформа | Windows | Архитектура |
|---|---|---|---|
| Modern | .NET 10 | Windows 10/11 | x64, x86 |
| Legacy | .NET 6 | Windows 7 SP1/8.1 | x64, x86 |

Готовые пакеты являются автономными: отдельно устанавливать .NET Runtime не требуется. Для полноценной обработки страниц нужен Chrome или Chromium. При его отсутствии MetricBot может предложить установить Chromium; на Windows 7/8.1 используется совместимая версия Chromium 109.

### Быстрый старт

1. Скачайте и распакуйте пакет, соответствующий вашей версии Windows и архитектуре процессора.
2. Запустите `MetricBot.exe`.
3. Откройте настройки и добавьте URL — по одному адресу на строку.
4. Задайте минимальный и максимальный интервал, а также режим обхода.
5. Сохраните настройки и нажмите **«Запустить»**.

При закрытии окна приложение остаётся в системном трее. Для полного завершения выберите выход в меню значка MetricBot в трее.

### Настройки и данные

- `config.json` — настройки приложения; создаётся рядом с исполняемым файлом.
- `metric_bot.log` — журнал событий по умолчанию; создаётся рядом с исполняемым файлом.
- `%LocalAppData%\MetricBot\security.json` — соль и хеш пароля. Сам пароль не сохраняется.

Если пароль забыт, защиту можно сбросить удалением `security.json`. Для сохранения настроек и журнала каталог приложения должен быть доступен пользователю для записи.

### Сборка из исходного кода

Требования: Windows и .NET 10 SDK.

```powershell
dotnet build MetricBot.csproj
```

Запуск Modern-варианта:

```powershell
dotnet run --project MetricBot.csproj --framework net10.0-windows
```

Создание автономных ZIP-пакетов Modern и Legacy для x64 и x86:

```powershell
.\Publish-MetricBot.ps1
```

Архивы будут сохранены в `publish\Release`.

---

## English

MetricBot is a Windows desktop application that automatically visits configured web pages at adjustable intervals. It runs in the background, supports JavaScript-enabled pages, and displays its activity in a log.

> Use MetricBot only with websites you own or are authorized to access. Follow website policies and applicable laws.

### Features

- visits a list of URLs using an installed Google Chrome or Chromium browser;
- can install a compatible Chromium version from the settings window;
- falls back to `HttpClient` when no browser is available (without JavaScript execution);
- three visit modes: all links, one link sequentially, or a random link;
- randomized delay within a configurable interval;
- TXT import and export for URL lists;
- launch at Windows startup, start minimized, and begin visiting automatically;
- system tray operation and start/stop controls;
- event log in both the user interface and a file;
- password-protected application controls;
- single-instance behavior.

### Compatibility

| Edition | Target | Windows | Architecture |
|---|---|---|---|
| Modern | .NET 10 | Windows 10/11 | x64, x86 |
| Legacy | .NET 6 | Windows 7 SP1/8.1 | x64, x86 |

Prebuilt packages are self-contained, so a separate .NET Runtime installation is not required. Chrome or Chromium is needed for full page processing. If neither is available, MetricBot can offer to install Chromium; Windows 7/8.1 uses the compatible Chromium 109 release.

### Quick start

1. Download and extract the package matching your Windows version and CPU architecture.
2. Run `MetricBot.exe`.
3. Open Settings and add URLs, one address per line.
4. Choose the minimum and maximum interval and a visit mode.
5. Save the settings and click **Start**.

Closing the window keeps the application running in the system tray. To quit completely, use the exit command in the MetricBot tray icon menu.

### Configuration and data

- `config.json` — application settings, created next to the executable.
- `metric_bot.log` — default event log, created next to the executable.
- `%LocalAppData%\MetricBot\security.json` — the password salt and hash. The password itself is not stored.

If the password is forgotten, protection can be reset by deleting `security.json`. The application directory must be writable by the current user so that settings and logs can be saved.

### Building from source

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet build MetricBot.csproj
```

Run the Modern target:

```powershell
dotnet run --project MetricBot.csproj --framework net10.0-windows
```

Create self-contained Modern and Legacy ZIP packages for x64 and x86:

```powershell
.\Publish-MetricBot.ps1
```

The archives are written to `publish\Release`.
