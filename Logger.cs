using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace MetricBot
{
    public enum LogLevel { Info, Ok, Warn, Error, Dim }

    public static class Logger
    {
        private static WpfRichTextBox? _box;
        private static string       _logFile = "";
        private static readonly object _fileLock = new();
        private static int _lineCount = 0;
        private static Paragraph? _progressParagraph;

        public static void Init(WpfRichTextBox box)
        {
            _box = box;

            // Убираем стартовый пустой абзац и внутренние отступы RichTextBox
            _box.Document.Blocks.Clear();
            _box.Document.PagePadding = new Thickness(0);
            _progressParagraph = null;
            _lineCount = 0;

            RefreshLogPath();
        }

        public static void RefreshLogPath()
        {
            var cfg = AppConfig.Current;
            _logFile = string.IsNullOrWhiteSpace(cfg.LogPath)
                ? Path.Combine(AppContext.BaseDirectory, "metric_bot.log")
                : cfg.LogPath;
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var line = BuildLine(message);

            // Пишем в файл
            WriteFile(line);

            // Пишем в UI
            if (_box != null)
                _box.Dispatcher.Invoke(() => AppendToBox(line, level));
        }

        // Обновляем одну и ту же строку в консоли, чтобы прогресс установки
        // не создавал тысячи строк в интерфейсе и файле лога.
        public static void Progress(string message, LogLevel level = LogLevel.Dim)
        {
            var line = BuildLine(message);

            if (_box != null)
                _box.Dispatcher.BeginInvoke(new Action(() => UpsertProgressLine(line, level)));
        }

        // Финальная строка прогресса: обновляет строку в консоли и один раз пишет в файл.
        public static void FinishProgress(string message, LogLevel level = LogLevel.Ok)
        {
            var line = BuildLine(message);
            WriteFile(line);

            if (_box != null)
            {
                _box.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpsertProgressLine(line, level);
                    _progressParagraph = null;
                }));
            }
        }

        private static string BuildLine(string message)
        {
            var ts = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            return $"[{ts}]  {message}";
        }

        private static void AppendToBox(string line, LogLevel level)
        {
            if (_box == null) return;

            var para = CreateParagraph(line, level);
            AddParagraph(para);
            _progressParagraph = null;
            _box.ScrollToEnd();
        }

        private static void UpsertProgressLine(string line, LogLevel level)
        {
            if (_box == null) return;

            if (_progressParagraph == null)
            {
                _progressParagraph = CreateParagraph(line, level);
                AddParagraph(_progressParagraph);
            }
            else
            {
                _progressParagraph.Inlines.Clear();
                _progressParagraph.Inlines.Add(new Run(line));
                _progressParagraph.Foreground = new SolidColorBrush(GetColor(level));
            }

            _box.ScrollToEnd();
        }

        private static void AddParagraph(Paragraph para)
        {
            if (_box == null) return;

            var cfg = AppConfig.Current;

            if (_lineCount >= cfg.MaxLogLines && cfg.MaxLogLines > 0)
            {
                var first = _box.Document.Blocks.FirstBlock;
                if (first != null)
                {
                    if (ReferenceEquals(first, _progressParagraph))
                        _progressParagraph = null;

                    _box.Document.Blocks.Remove(first);
                    _lineCount--;
                }
            }

            _box.Document.Blocks.Add(para);
            _lineCount++;
        }

        private static Paragraph CreateParagraph(string line, LogLevel level)
        {
            return new Paragraph(new Run(line))
            {
                Foreground = new SolidColorBrush(GetColor(level)),
                FontFamily = new WpfFontFamily("Consolas"),
                FontSize   = 11,
                Margin     = new Thickness(0),
                Padding    = new Thickness(0),
                LineHeight = 16,
            };
        }

        private static WpfColor GetColor(LogLevel level) => level switch
        {
            LogLevel.Ok    => WpfColor.FromRgb(0x39, 0xFF, 0x14),  // зелёный
            LogLevel.Warn  => WpfColor.FromRgb(0xFF, 0xD7, 0x00),  // жёлтый
            LogLevel.Error => WpfColor.FromRgb(0xFF, 0x44, 0x44),  // красный
            LogLevel.Dim   => WpfColor.FromRgb(0x55, 0x55, 0x55),  // серый
            _              => WpfColor.FromRgb(0x39, 0xFF, 0x14),  // зелёный по умолч.
        };

        private static void WriteFile(string line)
        {
            try
            {
                lock (_fileLock)
                    File.AppendAllText(_logFile, line + Environment.NewLine);
            }
            catch { /* не критично */ }
        }

        public static string LogFilePath => _logFile;
    }
}
