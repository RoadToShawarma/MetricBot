using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MetricBot
{
    public static class BotEngine
    {
        private const string ModernChromiumBuild = "1117"; // Chromium 125.0.6422.26
        private const string LegacyChromiumBuild = "1041"; // Chromium 109.x — последний для Windows 7/8/8.1

        private const string ChromiumZipName64 = "chromium-win64.zip";
        private const string ChromiumZipName32 = "chromium-win32.zip";

        // Виртуального времени достаточно, чтобы страница выполнила JS и отправила счётчики.
        private const int VirtualTimeBudgetMs = 12_000;
        private const int BrowserHardTimeoutMs = 45_000;

        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        })
        { Timeout = TimeSpan.FromSeconds(45) };

        static BotEngine()
        {
            Http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            Http.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
            Http.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        // ── Главный метод посещения ────────────────────────────────
        public static void VisitUrl(string url, CancellationToken ct)
        {
            if (!TryVisitChrome(url, ct) && !ct.IsCancellationRequested)
                TryVisitHttp(url, ct);
        }

        // ── Headless Chrome / Chromium ─────────────────────────────
        private static bool TryVisitChrome(string url, CancellationToken ct)
        {
            Process? proc = null;
            string? userDataDir = null;

            try
            {
                var browser = FindBrowser();
                if (browser == null)
                {
                    Logger.Log("Chrome/Chromium не найден, использую HttpClient", LogLevel.Dim);
                    return false;
                }

                // Отдельный временный профиль на каждый запуск исключает блокировку SingletonLock
                // и повреждение профиля после принудительного завершения старого Chrome.
                var profilesRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MetricBot",
                    "BrowserProfiles");

                Directory.CreateDirectory(profilesRoot);
                userDataDir = Path.Combine(profilesRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(userDataDir);

                var headlessArg = browser.UseClassicHeadless ? "--headless" : "--headless=new";

                // --dump-dom принципиально важен: Chrome выполняет JS, выводит итоговый DOM
                // и штатно завершает процесс. Без этого Chrome продолжает работать бесконечно,
                // после чего MetricBot убивает его и ошибочно принимает код -1 за сбой визита.
                var arguments = string.Join(" ", new[]
                {
                    headlessArg,
                    "--dump-dom",
                    "--disable-gpu",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-extensions",
                    "--disable-sync",
                    "--disable-background-timer-throttling",
                    "--disable-renderer-backgrounding",
                    "--disable-backgrounding-occluded-windows",
                    "--hide-scrollbars",
                    "--mute-audio",
                    $"--user-data-dir={QuoteArgument(userDataDir)}",
                    $"--virtual-time-budget={VirtualTimeBudgetMs}",
                    QuoteArgument(url),
                });

                var psi = new ProcessStartInfo
                {
                    FileName = browser.ExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                var stopwatch = Stopwatch.StartNew();
                proc = Process.Start(psi);
                if (proc == null)
                {
                    Logger.Log("Chrome/Chromium не запустился", LogLevel.Dim);
                    return false;
                }

                // Читаем ОБА потока. Если stdout перенаправлен, но не читается, большой DOM
                // способен заполнить буфер и навсегда заблокировать Chrome.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                var timedOut = false;
                while (!proc.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested)
                    {
                        KillProcess(proc);
                        return false;
                    }

                    if (stopwatch.ElapsedMilliseconds >= BrowserHardTimeoutMs)
                    {
                        timedOut = true;
                        KillProcess(proc);
                        break;
                    }
                }

                try { proc.WaitForExit(5000); } catch { }

                var stdout = GetTaskResult(stdoutTask);
                var stderr = GetTaskResult(stderrTask);

                if (ct.IsCancellationRequested)
                    return false;

                var navigationError = FindNavigationError(stdout, stderr);
                if (navigationError != null)
                {
                    Logger.Log($"{browser.DisplayName}: {navigationError}", LogLevel.Dim);
                    return false;
                }

                if (timedOut)
                {
                    // Если страница работала достаточно долго и Chrome не сообщил сетевую ошибку,
                    // визит уже мог быть отправлен. Не запускаем второй, ложный HttpClient-визит.
                    if (stopwatch.ElapsedMilliseconds >= VirtualTimeBudgetMs)
                    {
                        Logger.Log(
                            $"✓  {url}  ({browser.DisplayName}, JS выполнен; процесс закрыт по тайм-ауту)",
                            LogLevel.Ok);
                        return true;
                    }

                    Logger.Log($"{browser.DisplayName}: превышено время запуска", LogLevel.Dim);
                    return false;
                }

                var exitCode = TryGetExitCode(proc);
                if (exitCode != 0)
                {
                    var message = FindMeaningfulBrowserMessage(stderr)
                                  ?? $"код выхода {exitCode}";
                    Logger.Log($"{browser.DisplayName}: {message}", LogLevel.Dim);
                    return false;
                }

                // Для --dump-dom непустой stdout означает, что Chrome загрузил страницу,
                // выполнил сценарии и сериализовал итоговый DOM.
                if (string.IsNullOrWhiteSpace(stdout))
                {
                    Logger.Log($"{browser.DisplayName}: страница не вернула DOM", LogLevel.Dim);
                    return false;
                }

                Logger.Log($"✓  {url}  ({browser.DisplayName}, JS выполнен)", LogLevel.Ok);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"Chrome/Chromium: {FirstLine(ex.Message)}", LogLevel.Dim);
                return false;
            }
            finally
            {
                KillProcess(proc);
                proc?.Dispose();

                if (!string.IsNullOrWhiteSpace(userDataDir))
                    TryDeleteDirectory(userDataDir);
            }
        }

        // ── HttpClient fallback ────────────────────────────────────
        private static void TryVisitHttp(string url, CancellationToken ct)
        {
            try
            {
                using var resp = Http.GetAsync(url, ct).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                var body = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();

                var match = Regex.Match(
                    body,
                    @"<title[^>]*>(.*?)</title>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                var title = match.Success
                    ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim())
                    : url;

                ct.WaitHandle.WaitOne(new Random().Next(2000, 5000));
                Logger.Log($"✓  {url}  —  «{title}» (HttpClient, без JS)", LogLevel.Ok);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"Ошибка: {url}  —  {FirstLine(ex.Message)}. " +
                    "Если это Windows 7/8, установите Chromium через настройки MetricBot.",
                    LogLevel.Error);
            }
        }

        // ── Поиск браузера на всех версиях Windows ─────────────────
        private static BrowserLaunchInfo? FindBrowser()
        {
            var candidates = new List<(string? Path, string Name)>();

            // Сначала используем уже установленный браузер — одинаково на Windows 7–11.
            candidates.Add((FindInstalledChromeExe(), "Google Chrome"));
            candidates.Add((FindInstalledChromiumExe(), "Chromium"));

            // Затем ищем Chromium, установленный самим MetricBot.
            candidates.Add((FindManagedChromiumExe(IsLegacyWindows()), "Chromium MetricBot"));

            // Совместимость с предыдущими версиями программы, которые ставили браузер
            // в каталог ms-playwright.
            candidates.Add((FindPlaywrightChromiumExe(), "Chromium MetricBot"));

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Path) || !File.Exists(candidate.Path))
                    continue;

                var info = CreateBrowserLaunchInfo(candidate.Path, candidate.Name);

                // Chromium новее 109 не запускается на Windows 7/8/8.1.
                if (IsLegacyWindows() && info.MajorVersion > 109)
                    continue;

                return info;
            }

            return null;
        }

        // Старое публичное имя оставлено, чтобы не ломать остальной код.
        public static string? FindChromeExe() => FindBrowser()?.ExePath;

        public static bool IsChromiumInstalled() => FindBrowser() != null;

        public static BrowserStatusInfo GetBrowserStatus()
        {
            var windowsName = GetWindowsName();
            var browser = FindBrowser();

            if (browser != null)
            {
                var suffix = string.IsNullOrWhiteSpace(browser.Version)
                    ? ""
                    : $" — {browser.Version}";

                return new BrowserStatusInfo
                {
                    IsAvailable = true,
                    CanAutoInstall = false,
                    Text = browser.UseClassicHeadless
                        ? $"✓  {windowsName}: найден {browser.DisplayName}{suffix}. JS-режим совместимости активен"
                        : $"✓  {windowsName}: найден {browser.DisplayName}{suffix}. JS-режим активен",
                };
            }

            return new BrowserStatusInfo
            {
                IsAvailable = false,
                CanAutoInstall = true,
                Text = IsLegacyWindows()
                    ? $"⚠  {windowsName}: Chrome/Chromium не найден. Можно установить Chromium 109"
                    : $"⚠  {windowsName}: Chrome/Chromium не найден. Можно установить Chromium",
            };
        }

        // ── Установка Chromium ────────────────────────────────────
        public static async Task InstallChromium(Action<string> onStatus, CancellationToken ct = default)
        {
            var isLegacy = IsLegacyWindows();
            var build = isLegacy ? LegacyChromiumBuild : ModernChromiumBuild;
            var zipName = GetChromiumZipName();
            var title = isLegacy ? "Chromium 109" : "Chromium";

            var urls = new[]
            {
                $"https://playwright-akamai.azureedge.net/builds/chromium/{build}/{zipName}",
                $"https://playwright.azureedge.net/builds/chromium/{build}/{zipName}",
            };

            var metricBotPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MetricBot");

            var chromiumDir = Path.Combine(
                metricBotPath,
                isLegacy ? "Chromium109" : "Chromium");

            var zipPath = Path.Combine(
                Path.GetTempPath(),
                $"metricbot-chromium-{build}-{zipName}");

            Directory.CreateDirectory(metricBotPath);

            try
            {
                Exception? lastException = null;

                foreach (var downloadUrl in urls)
                {
                    try
                    {
                        Logger.Log($"Скачиваю {title}", LogLevel.Dim);
                        onStatus($"Скачиваю {title}...");

                        using var response = await Http.GetAsync(
                                downloadUrl,
                                HttpCompletionOption.ResponseHeadersRead,
                                ct)
                            .ConfigureAwait(false);

                        response.EnsureSuccessStatusCode();

                        var total = response.Content.Headers.ContentLength ?? 0;
                        var buffer = new byte[81920];
                        long downloaded = 0;
                        var lastPercent = -1;
                        long lastMb = -1;

                        await using var stream = await response.Content.ReadAsStreamAsync(ct)
                            .ConfigureAwait(false);
                        await using var file = File.Create(zipPath);

                        int read;
                        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            downloaded += read;

                            if (total > 0)
                            {
                                var percent = (int)(downloaded * 100 / total);
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    onStatus(
                                        $"Загрузка {title}: {percent}%  " +
                                        $"({downloaded / 1048576} МБ / {total / 1048576} МБ)");
                                }
                            }
                            else
                            {
                                var mb = downloaded / 1048576;
                                if (mb != lastMb)
                                {
                                    lastMb = mb;
                                    onStatus($"Загрузка {title}: {mb} МБ");
                                }
                            }
                        }

                        lastException = null;
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastException = ex;
                    }
                }

                ct.ThrowIfCancellationRequested();

                if (lastException != null)
                    throw new Exception($"Не удалось скачать {title}: {lastException.Message}");

                onStatus($"Распаковываю {title}...");

                await Task.Run(() =>
                {
                    if (Directory.Exists(chromiumDir))
                        Directory.Delete(chromiumDir, recursive: true);

                    ZipFile.ExtractToDirectory(zipPath, chromiumDir);
                    FlattenChromiumArchive(chromiumDir);
                }, ct).ConfigureAwait(false);

                onStatus($"✓  {title} установлен");
            }
            finally
            {
                try { File.Delete(zipPath); } catch { }
            }
        }

        private static void FlattenChromiumArchive(string chromiumDir)
        {
            var possibleInnerDirectories = new[]
            {
                "chrome-win",
                "chrome-win32",
                "chrome-win64",
            };

            var innerDir = possibleInnerDirectories
                .Select(name => Path.Combine(chromiumDir, name))
                .FirstOrDefault(Directory.Exists);

            if (innerDir == null)
                return;

            foreach (var file in Directory.GetFiles(innerDir))
            {
                File.Move(
                    file,
                    Path.Combine(chromiumDir, Path.GetFileName(file)),
                    overwrite: true);
            }

            foreach (var directory in Directory.GetDirectories(innerDir))
            {
                var destination = Path.Combine(chromiumDir, Path.GetFileName(directory));
                if (Directory.Exists(destination))
                    Directory.Delete(destination, true);

                Directory.Move(directory, destination);
            }

            Directory.Delete(innerDir, true);
        }

        private static string GetChromiumZipName() =>
            RuntimeInformation.OSArchitecture == Architecture.X86
                ? ChromiumZipName32
                : ChromiumZipName64;

        // ── Пути к браузерам ───────────────────────────────────────
        private static string? FindInstalledChromeExe()
        {
            var candidates = new List<string?>
            {
                ReadAppPath("chrome.exe"),
                ReadRegistryDefaultValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"),

                Path.Combine(GetFolder(Environment.SpecialFolder.ProgramFiles),
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(GetFolder(Environment.SpecialFolder.ProgramFilesX86),
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(GetFolder(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "Application", "chrome.exe"),
            };

            return FirstExistingFile(candidates);
        }

        private static string? FindInstalledChromiumExe()
        {
            var programFiles = GetFolder(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = GetFolder(Environment.SpecialFolder.ProgramFilesX86);
            var local = GetFolder(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = AppContext.BaseDirectory;

            var candidates = new List<string?>
            {
                ReadAppPath("chromium.exe"),
                ReadRegistryDefaultValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chromium.exe"),

                Path.Combine(programFiles, "Chromium", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Chromium", "Application", "chrome.exe"),
                Path.Combine(local, "Chromium", "Application", "chrome.exe"),

                Path.Combine(programFiles, "Chromium", "chrome.exe"),
                Path.Combine(programFilesX86, "Chromium", "chrome.exe"),
                Path.Combine(local, "Chromium", "chrome.exe"),

                // Portable Chromium рядом с приложением.
                Path.Combine(baseDir, "chrome.exe"),
                Path.Combine(baseDir, "Chromium", "chrome.exe"),
                Path.Combine(baseDir, "Chromium", "Application", "chrome.exe"),
                Path.Combine(baseDir, "Browsers", "Chromium", "chrome.exe"),
                Path.Combine(baseDir, "Browsers", "Chromium", "Application", "chrome.exe"),
            };

            return FirstExistingFile(candidates);
        }

        private static string? FindManagedChromiumExe(bool legacy)
        {
            var local = GetFolder(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = AppContext.BaseDirectory;
            var folderName = legacy ? "Chromium109" : "Chromium";

            var roots = new[]
            {
                Path.Combine(local, "MetricBot", folderName),
                Path.Combine(baseDir, folderName),
                Path.Combine(baseDir, "Browsers", folderName),

                // Старые имена каталогов.
                Path.Combine(local, "MetricBot", "Chrome109"),
                Path.Combine(local, "MetricBot", "LegacyChromium"),
                Path.Combine(baseDir, "Chrome109"),
                Path.Combine(baseDir, "LegacyChromium"),
                Path.Combine(baseDir, "Browsers", "Chrome109"),
                Path.Combine(baseDir, "Browsers", "LegacyChromium"),
            };

            return FindChromeExeUnderRoots(roots);
        }

        private static string? FindPlaywrightChromiumExe()
        {
            var browsersPath = Path.Combine(
                GetFolder(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");

            if (!Directory.Exists(browsersPath))
                return null;

            try
            {
                return Directory.GetDirectories(browsersPath, "chromium*")
                    .OrderByDescending(Path.GetFileName)
                    .SelectMany(directory => new[]
                    {
                        Path.Combine(directory, "chrome.exe"),
                        Path.Combine(directory, "chrome-win", "chrome.exe"),
                        Path.Combine(directory, "chrome-win32", "chrome.exe"),
                        Path.Combine(directory, "chrome-win64", "chrome.exe"),
                    })
                    .FirstOrDefault(File.Exists);
            }
            catch
            {
                return null;
            }
        }

        private static string? FindChromeExeUnderRoots(IEnumerable<string> roots)
        {
            return roots
                .SelectMany(root => new[]
                {
                    Path.Combine(root, "chrome.exe"),
                    Path.Combine(root, "chrome-win", "chrome.exe"),
                    Path.Combine(root, "chrome-win32", "chrome.exe"),
                    Path.Combine(root, "chrome-win64", "chrome.exe"),
                    Path.Combine(root, "Application", "chrome.exe"),
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists);
        }

        private static string? ReadAppPath(string executableName)
        {
            var keys = new[]
            {
                $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}",
                $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}",
            };

            foreach (var key in keys)
            {
                try
                {
                    var value = Registry.GetValue(key, "", null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim().Trim('"');
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? ReadRegistryDefaultValue(string key)
        {
            try
            {
                var value = Registry.GetValue(key, "", null) as string;
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : value.Trim().Trim('"');
            }
            catch
            {
                return null;
            }
        }

        private static string? FirstExistingFile(IEnumerable<string?> candidates) =>
            candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Trim().Trim('"'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists);

        private static BrowserLaunchInfo CreateBrowserLaunchInfo(
            string exePath,
            string fallbackDisplayName)
        {
            var version = GetBrowserVersion(exePath);
            var majorVersion = GetMajorVersion(version);
            var productName = GetBrowserProductName(exePath);

            var displayName = string.IsNullOrWhiteSpace(productName)
                ? fallbackDisplayName
                : productName;

            return new BrowserLaunchInfo
            {
                ExePath = exePath,
                DisplayName = displayName,
                Version = version,
                MajorVersion = majorVersion,
                UseClassicHeadless = IsLegacyWindows() || (majorVersion > 0 && majorVersion < 112),
            };
        }

        private static string? GetBrowserVersion(string exePath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                return !string.IsNullOrWhiteSpace(info.ProductVersion)
                    ? info.ProductVersion
                    : info.FileVersion;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetBrowserProductName(string exePath)
        {
            try
            {
                var productName = FileVersionInfo.GetVersionInfo(exePath).ProductName;
                if (string.IsNullOrWhiteSpace(productName))
                    return null;

                if (productName.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
                    return "Chromium";
                if (productName.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                    return "Google Chrome";

                return productName;
            }
            catch
            {
                return null;
            }
        }

        private static int GetMajorVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return 0;

            var firstPart = version.Split('.')[0];
            return int.TryParse(firstPart, out var major) ? major : 0;
        }

        private static string GetFolder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch { return ""; }
        }

        // ── Версия Windows ─────────────────────────────────────────
        public static bool IsLegacyWindows()
        {
            var version = GetRealWindowsVersion();
            return version.Major == 6 && version.Minor is >= 1 and <= 3;
        }

        private static string GetWindowsName()
        {
            var version = GetRealWindowsVersion();
            return version switch
            {
                { Major: 10, Build: >= 22000 } => "Windows 11",
                { Major: 10 } => "Windows 10",
                { Major: 6, Minor: 3 } => "Windows 8.1",
                { Major: 6, Minor: 2 } => "Windows 8",
                { Major: 6, Minor: 1 } => "Windows 7",
                _ => $"Windows {version.Major}.{version.Minor}.{version.Build}",
            };
        }

        private static Version GetRealWindowsVersion()
        {
            try
            {
                var os = new RTL_OSVERSIONINFOEX
                {
                    dwOSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOEX>(),
                    szCSDVersion = "",
                };

                if (RtlGetVersion(ref os) == 0)
                {
                    return new Version(
                        (int)os.dwMajorVersion,
                        (int)os.dwMinorVersion,
                        (int)os.dwBuildNumber);
                }
            }
            catch
            {
            }

            return Environment.OSVersion.Version;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOEX
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;

            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        // ── Вспомогательные методы ─────────────────────────────────
        private static string QuoteArgument(string value) =>
            "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string GetTaskResult(Task<string> task)
        {
            try { return task.GetAwaiter().GetResult(); }
            catch { return ""; }
        }

        private static int TryGetExitCode(Process process)
        {
            try { return process.HasExited ? process.ExitCode : -1; }
            catch { return -1; }
        }

        private static string? FindNavigationError(string stdout, string stderr)
        {
            var combined = stdout + "\n" + stderr;
            var patterns = new[]
            {
                "chrome-error://chromewebdata",
                "ERR_NAME_NOT_RESOLVED",
                "ERR_CONNECTION_REFUSED",
                "ERR_CONNECTION_RESET",
                "ERR_CONNECTION_CLOSED",
                "ERR_CONNECTION_TIMED_OUT",
                "ERR_TIMED_OUT",
                "ERR_ADDRESS_UNREACHABLE",
                "ERR_INTERNET_DISCONNECTED",
                "ERR_PROXY_CONNECTION_FAILED",
                "ERR_TUNNEL_CONNECTION_FAILED",
                "ERR_SSL_",
                "ERR_CERT_",
            };

            var found = patterns.FirstOrDefault(pattern =>
                combined.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            return found == null ? null : $"ошибка загрузки {found}";
        }

        private static string? FindMeaningfulBrowserMessage(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return null;

            var ignoredFragments = new[]
            {
                "JQMIGRATE:",
                "INFO:CONSOLE",
                "DevTools listening on",
                "Created TensorFlow Lite",
            };

            return stderr
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line =>
                    line.Length > 0 &&
                    !ignoredFragments.Any(fragment =>
                        line.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        }

        private static string FirstLine(string value) =>
            value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
            ?? value;

        private static void KillProcess(Process? process)
        {
            if (process == null)
                return;

            try
            {
                if (process.HasExited)
                    return;

                process.Kill(entireProcessTree: true);
            }
            catch
            {
                try { process.Kill(); } catch { }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                    return;
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }

        private sealed class BrowserLaunchInfo
        {
            public string ExePath { get; init; } = "";
            public string DisplayName { get; init; } = "Chrome/Chromium";
            public string? Version { get; init; }
            public int MajorVersion { get; init; }
            public bool UseClassicHeadless { get; init; }
        }

        public sealed class BrowserStatusInfo
        {
            public bool IsAvailable { get; init; }
            public bool CanAutoInstall { get; init; }
            public string Text { get; init; } = "";
        }
    }
}
