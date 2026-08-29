using System.Diagnostics;
using System.Drawing;
using System.IO;
using WinForms = System.Windows.Forms;

namespace MetricBot
{
    public class TrayService : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly WinForms.ToolStripMenuItem _miStart;
        private readonly WinForms.ToolStripMenuItem _miStop;
        private readonly Icon _appIcon;

        public event Action? OnShow;
        public event Action? OnStart;
        public event Action? OnStop;
        public event Action? OnExit;

        public TrayService()
        {
            _appIcon = LoadApplicationIcon();

            _miStart = new WinForms.ToolStripMenuItem("▶  Запустить",  null, (_, _) => OnStart?.Invoke()) { Enabled = true };
            _miStop  = new WinForms.ToolStripMenuItem("■  Остановить", null, (_, _) => OnStop?.Invoke())  { Enabled = false };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add(new WinForms.ToolStripMenuItem("Открыть", null, (_, _) => OnShow?.Invoke()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(_miStart);
            menu.Items.Add(_miStop);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(new WinForms.ToolStripMenuItem("Выход", null, (_, _) => OnExit?.Invoke()));

            _icon = new WinForms.NotifyIcon
            {
                Text             = "MetricBot",
                Visible          = true,
                ContextMenuStrip = menu,
                Icon             = _appIcon,
            };
            _icon.DoubleClick += (_, _) => OnShow?.Invoke();
        }

        public void SetRunning(bool running)
        {
            // Иконку не меняем: в трее используется тот же значок, что и у приложения.
            _miStart.Enabled = !running;
            _miStop.Enabled  = running;
        }

        public void Notify(string msg) =>
            _icon.ShowBalloonTip(1500, "MetricBot", msg, WinForms.ToolTipIcon.Info);

        private static Icon LoadApplicationIcon()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                        return (Icon)icon.Clone();
                }
            }
            catch { }

            try
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(icoPath))
                    return new Icon(icoPath);
            }
            catch { }

            // Фолбэк на старое расположение — можно удалить после того,
            // как icon.ico будет перенесён в Assets.
            try
            {
                var legacyIcoPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (File.Exists(legacyIcoPath))
                    return new Icon(legacyIcoPath);
            }
            catch { }

            return BuildFallbackIcon();
        }

        private static Icon BuildFallbackIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g   = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A));
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var font   = new Font("Consolas", 13, FontStyle.Bold);
            using var fBrush = new SolidBrush(System.Drawing.Color.FromArgb(0x39, 0xFF, 0x14));
            g.DrawString("M", font, fBrush, 5f, 5f);

            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _appIcon.Dispose();
        }
    }
}
