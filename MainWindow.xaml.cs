using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using WpfColor = System.Windows.Media.Color;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;

namespace MetricBot
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private bool     _running;
        private TrayService? _tray;
        private int      _seqIndex = 0;   // для Sequential режима
        private bool     _isExitRequested;
        private bool     _isLocked;
        private bool     _unlockDialogOpen;
        private AboutWindow? _aboutWindow;

        // ── Автозапуск ────────────────────────────────────────────
        private const string AutorunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName    = "MetricBot";

        public static bool IsAutorunSet()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutorunKey);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        public static void SetAutorun(bool enable)
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutorunKey, writable: true)
                ?? throw new InvalidOperationException(
                    $"Не удалось открыть раздел реестра HKCU\\{AutorunKey} для записи.");

            if (enable)
                key.SetValue(AppName,
                    $"\"{Process.GetCurrentProcess().MainModule!.FileName}\"");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }

        // ══════════════════════════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();
            AppConfig.Load();
            Logger.Init(LogBox);

            SetStatus("ГОТОВ К РАБОТЕ", HexColor("#FFD700"));
            TxtNext.Text = "";

            _tray = new TrayService();
            _tray.OnShow  += ShowWindow;
            _tray.OnStart += () => Dispatcher.Invoke(() => RunUnlocked(() => _ = StartAsync()));
            _tray.OnStop  += () => Dispatcher.Invoke(() => RunUnlocked(Stop));
            _tray.OnLock  += () => Dispatcher.Invoke(Lock);
            _tray.OnExit  += () => Dispatcher.Invoke(() => RunUnlocked(ExitApp));

            Closing += OnWindowClosing;
            ContentRendered += OnContentRendered;

            // Проверка браузера с учётом версии Windows
            var browserStatus = BotEngine.GetBrowserStatus();
            Logger.Log(browserStatus.Text, browserStatus.IsAvailable ? LogLevel.Ok : LogLevel.Warn);

            if (AppConfig.Current.Autostart)
                Loaded += (_, _) => _ = StartAsync();

            _isLocked = PasswordService.IsPasswordSet;
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnContentRendered;
            var cfg = AppConfig.Current;
            if (_isLocked)
            {
                Hide();
                if (RequestUnlock())
                    BringToFront();
            }
            else if (cfg.StartMinimized)
            {
                Hide();
                _tray?.Notify("MetricBot запущен в трее");
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExitRequested)
                return;

            e.Cancel = true;
            if (PasswordService.IsPasswordSet)
                Lock();
            else
                Hide();
        }

        private void ShowWindow() => Dispatcher.Invoke(() =>
        {
            if (!_isLocked || RequestUnlock())
                BringToFront();
        });

        private void Lock()
        {
            if (!PasswordService.IsPasswordSet)
                return;

            _isLocked = true;

            foreach (Window ownedWindow in OwnedWindows.Cast<Window>().ToArray())
                ownedWindow.Close();

            Hide();
            _tray?.Notify("MetricBot заблокирован");
        }

        private bool RequestUnlock()
        {
            if (!_isLocked)
                return true;
            if (_unlockDialogOpen)
                return false;

            _unlockDialogOpen = true;
            try
            {
                var dialog = new PasswordWindow(PasswordWindow.PasswordMode.Unlock);
                if (dialog.ShowDialog() == true)
                {
                    _isLocked = false;
                    return true;
                }
                return false;
            }
            finally
            {
                _unlockDialogOpen = false;
            }
        }

        private void RunUnlocked(Action action)
        {
            if (!_isLocked || RequestUnlock())
                action();
        }

        public void BringToFront()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(BringToFront);
                return;
            }

            if (_isLocked && !RequestUnlock())
                return;

            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                ShowWindowAsync(handle, SW_RESTORE);
                SetForegroundWindow(handle);
            }

            Activate();

            // Маленький трюк, чтобы окно точно вышло поверх остальных окон.
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ExitApp()
        {
            _isExitRequested = true;
            _cts?.Cancel();
            _tray?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        // ── Кнопки ────────────────────────────────────────────────
        private void BtnToggle_Click(object s, RoutedEventArgs e)
        {
            if (_running)
                Stop();
            else
                _ = StartAsync();
        }

        private void BtnSettings_Click(object s, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

        private void BtnAbout_Click(object s, RoutedEventArgs e)
        {
            _aboutWindow = new AboutWindow { Owner = this };
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Show();
        }

        private void MainWindow_PreviewMouseDown(object s, MouseButtonEventArgs e)
        {
            _aboutWindow?.Close();
        }

        private void BtnOpenLog_Click(object s, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(Logger.LogFilePath) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Log($"Не удалось открыть лог: {ex.Message}", LogLevel.Error); }
        }

        // ── Бот ───────────────────────────────────────────────────
        private async Task StartAsync()
        {
            if (_running) return;

            var cfg  = AppConfig.Current;
            var urls = cfg.Urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls.Count == 0)
            {
                Logger.Log("Нет URL для посещения. Откройте Настройки.", LogLevel.Warn);
                SetStatus("ГОТОВ К РАБОТЕ", HexColor("#FFD700"));
                TxtNext.Text = "";
                return;
            }

            _cts     = new CancellationTokenSource();
            _running = true;
            _tray?.SetRunning(true);

            BtnToggle.Content = "Остановить";
            BtnToggle.IsEnabled = true;
            SetStatus("РАБОТАЕТ", Colors.LimeGreen);
            TxtNext.Text = "";

            Logger.Log($"Старт. URL-ов: {urls.Count}, интервал: {cfg.MinInterval}–{cfg.MaxInterval} мин.", LogLevel.Ok);

            await Task.Run(() => BotLoop(urls, _cts.Token));
        }

        private void Stop()
        {
            if (!_running) return;
            BtnToggle.Content = "Остановка...";
            BtnToggle.IsEnabled = false;
            _cts?.Cancel();
        }

        private void BotLoop(List<string> urls, CancellationToken ct)
        {
            var cfg = AppConfig.Current;
            var rnd = new Random();

            while (!ct.IsCancellationRequested)
            {
                var toVisit = cfg.VisitMode switch
                {
                    VisitMode.All        => urls,
                    VisitMode.Sequential => new List<string> { urls[_seqIndex++ % urls.Count] },
                    VisitMode.Random     => new List<string> { urls[rnd.Next(urls.Count)] },
                    _                    => urls,
                };

                // Интервал всегда считается после выбранного обхода:
                // All — после всех ссылок, Sequential/Random — после одной выбранной ссылки.
                foreach (var url in toVisit)
                {
                    if (ct.IsCancellationRequested) break;
                    Logger.Log($"Открываю: {url}", LogLevel.Dim);
                    BotEngine.VisitUrl(url, ct);
                }

                if (!ct.IsCancellationRequested)
                {
                    int w = rnd.Next(cfg.MinInterval * 60, cfg.MaxInterval * 60 + 1);
                    SetNextStatus(w, ct);
                }
            }

            Logger.Log("Бот остановлен.", LogLevel.Warn);
            Dispatcher.Invoke(() =>
            {
                _running           = false;
                BtnToggle.Content  = "Запустить";
                BtnToggle.IsEnabled = true;
                TxtNext.Text       = "";
                SetStatus("ОСТАНОВЛЕН", HexColor("#FFD700"));
                _tray?.SetRunning(false);
            });
        }

        private void SetNextStatus(int waitSec, CancellationToken ct)
        {
            Logger.Log($"Следующий обход через {waitSec / 60} мин. {waitSec % 60} сек.", LogLevel.Dim);

            for (var left = waitSec; left > 0 && !ct.IsCancellationRequested; left--)
            {
                var countdown = FormatCountdown(left);
                Dispatcher.Invoke(() =>
                {
                    SetStatus("РАБОТАЕТ", Colors.LimeGreen);
                    TxtNext.Text = $"⏰ До следующего обхода: {countdown}";
                });

                if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                    break;
            }

            if (!ct.IsCancellationRequested)
            {
                Dispatcher.Invoke(() => TxtNext.Text = "");
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private static string FormatCountdown(int seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }

        private static WpfColor HexColor(string hex) =>
            (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        private void SetStatus(string text, System.Windows.Media.Color color)
        {
            var brush = new SolidColorBrush(color);
            StatusLabel.Text       = text;
            StatusLabel.Foreground = brush;
            StatusDot.Fill         = brush;
        }
    }
}
