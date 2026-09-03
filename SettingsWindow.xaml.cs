using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Win32SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MetricBot
{
    public partial class SettingsWindow : Window
    {
        private CancellationTokenSource? _installCts;

        public SettingsWindow()
        {
            InitializeComponent();

            // На низких разрешениях окно не вылезает за экран,
            // а содержимое прокручивается внутри ScrollViewer.
            MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);

            LoadFromConfig();
        }

        private void LoadFromConfig()
        {
            var cfg = AppConfig.Current;
            TxtUrls.Text    = string.Join(Environment.NewLine, cfg.Urls);
            TxtMin.Text     = cfg.MinInterval.ToString();
            TxtMax.Text     = cfg.MaxInterval.ToString();
            TxtMaxLines.Text = cfg.MaxLogLines.ToString();

            RbAll.IsChecked        = cfg.VisitMode == VisitMode.All;
            RbSequential.IsChecked = cfg.VisitMode == VisitMode.Sequential;
            RbRandom.IsChecked     = cfg.VisitMode == VisitMode.Random;

            ChkAutorun.IsChecked  = MainWindow.IsAutorunSet();
            ChkMinimized.IsChecked = cfg.StartMinimized;
            ChkAutostart.IsChecked = cfg.Autostart;
            UpdateMinimizedState();

            UpdateChromiumStatus();
            UpdatePasswordStatus();
        }

        private void UpdatePasswordStatus()
        {
            var enabled = PasswordService.IsPasswordSet;
            TxtPasswordStatus.Text = enabled
                ? "Пароль установлен. Управление приложением защищено."
                : "Пароль не установлен.";
            BtnSetPassword.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            BtnChangePassword.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            BtnRemovePassword.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSetPassword_Click(object s, RoutedEventArgs e)
        {
            if (new PasswordWindow(PasswordWindow.PasswordMode.Create) { Owner = this }.ShowDialog() == true)
                UpdatePasswordStatus();
        }

        private void BtnChangePassword_Click(object s, RoutedEventArgs e)
        {
            if (new PasswordWindow(PasswordWindow.PasswordMode.Change) { Owner = this }.ShowDialog() == true)
                UpdatePasswordStatus();
        }

        private void BtnRemovePassword_Click(object s, RoutedEventArgs e)
        {
            if (new PasswordWindow(PasswordWindow.PasswordMode.Remove) { Owner = this }.ShowDialog() == true)
                UpdatePasswordStatus();
        }

        private void UpdateChromiumStatus()
        {
            var status = BotEngine.GetBrowserStatus();

            TxtChromiumStatus.Text       = status.Text;
            TxtChromiumStatus.Foreground = (System.Windows.Media.Brush)FindResource(
                status.IsAvailable ? "AccentBrush" : "WarnBrush");
            BtnInstall.Visibility        = status.CanAutoInstall ? Visibility.Visible : Visibility.Collapsed;
            BtnInstall.Content           = BotEngine.IsLegacyWindows()
                ? "Установить Chromium 109"
                : "Установить Chromium";
        }

        // ── Автозапуск ────────────────────────────────────────────
        private void ChkAutorun_Changed(object s, RoutedEventArgs e)
        {
            UpdateMinimizedState();
        }

        private void UpdateMinimizedState()
        {
            ChkMinimized.IsEnabled = ChkAutorun.IsChecked == true;
            if (ChkAutorun.IsChecked != true)
                ChkMinimized.IsChecked = false;
        }

        // ── Импорт / Экспорт URL ──────────────────────────────────
        private void BtnImport_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Win32OpenFileDialog
            {
                Title  = "Загрузить список URL",
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dlg.FileName)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToArray();
                    TxtUrls.Text = string.Join(Environment.NewLine, lines);
                    Logger.Log($"Загружено {lines.Length} URL из {Path.GetFileName(dlg.FileName)}", LogLevel.Ok);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Ошибка загрузки: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private void BtnExport_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Win32SaveFileDialog
            {
                Title      = "Сохранить список URL",
                Filter     = "Текстовые файлы (*.txt)|*.txt",
                FileName   = "urls.txt",
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, TxtUrls.Text);
                    Logger.Log($"Список сохранён: {Path.GetFileName(dlg.FileName)}", LogLevel.Ok);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Ошибка сохранения: {ex.Message}", LogLevel.Error);
                }
            }
        }

        // ── Установка Chromium ────────────────────────────────────
        private async void BtnInstall_Click(object s, RoutedEventArgs e)
        {
            BtnInstall.IsEnabled          = false;
            TxtInstallProgress.Visibility = Visibility.Visible;
            TxtInstallProgress.Text       = "Подготовка к установке...";

            var installTitle = BotEngine.IsLegacyWindows() ? "Chromium 109" : "Chromium";
            Logger.Progress($"Установка {installTitle}: подготовка...", LogLevel.Dim);

            _installCts = new CancellationTokenSource();
            try
            {
                await BotEngine.InstallChromium(
                    status =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TxtInstallProgress.Text = status;
                            Logger.Progress($"Установка {installTitle}: {status}", LogLevel.Dim);
                        }));
                    },
                    _installCts.Token);

                TxtInstallProgress.Text = "✓  Установка завершена";
                Logger.FinishProgress($"{installTitle} успешно установлен!", LogLevel.Ok);
                UpdateChromiumStatus();
            }
            catch (OperationCanceledException)
            {
                TxtInstallProgress.Text = "Установка отменена";
                Logger.FinishProgress($"Установка {installTitle} отменена.", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                TxtInstallProgress.Text = $"✗  Ошибка: {ex.Message}";
                Logger.FinishProgress($"Ошибка установки {installTitle}: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                _installCts?.Dispose();
                _installCts = null;
                UpdateChromiumStatus();
            }
        }

        // ── Сохранить / Отмена ────────────────────────────────────
        private void BtnSave_Click(object s, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtMin.Text, out int mn)) mn = 31;
            if (!int.TryParse(TxtMax.Text, out int mx)) mx = 39;
            if (mn > mx) { (mn, mx) = (mx, mn); }
            if (!int.TryParse(TxtMaxLines.Text, out int maxLines)) maxLines = 500;

            var enableAutorun = ChkAutorun.IsChecked == true;
            try
            {
                MainWindow.SetAutorun(enableAutorun);
            }
            catch (Exception ex)
            {
                var permissionsHint = ex is UnauthorizedAccessException or System.Security.SecurityException
                    ? "\n\nВозможно, у текущего пользователя недостаточно прав для изменения этого раздела реестра."
                    : string.Empty;

                System.Windows.MessageBox.Show(
                    $"Не удалось {(enableAutorun ? "включить" : "отключить")} автозапуск программы.\n\n" +
                    $"Раздел: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\n" +
                    $"Параметр: MetricBot\n" +
                    $"Ошибка: {ex.GetType().Name}: {ex.Message}" +
                    permissionsHint,
                    "Ошибка сохранения автозапуска",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var cfg = AppConfig.Current;
            cfg.Urls = TxtUrls.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();

            cfg.MinInterval  = mn;
            cfg.MaxInterval  = mx;
            cfg.MaxLogLines  = maxLines;
            cfg.StartMinimized = ChkMinimized.IsChecked == true;
            cfg.Autostart    = ChkAutostart.IsChecked == true;

            cfg.VisitMode = RbSequential.IsChecked == true ? VisitMode.Sequential
                          : RbRandom.IsChecked      == true ? VisitMode.Random
                          : VisitMode.All;


            AppConfig.Save();
            Logger.RefreshLogPath();
            Logger.Log("Настройки сохранены", LogLevel.Ok);
            DialogResult = true;
        }

        private void BtnCancel_Click(object s, RoutedEventArgs e) => DialogResult = false;

        private void NumOnly(object s, TextCompositionEventArgs e) =>
            e.Handled = !e.Text.All(char.IsDigit);
    }
}
