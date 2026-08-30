using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MetricBot.Launcher;

internal static class Program
{
    private const uint MbIconError = 0x00000010;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var windows = GetWindowsVersion();
            var appFolder = IsLegacyWindows(windows) ? "Legacy" : "Modern";
            var executable = Path.Combine(AppContext.BaseDirectory, appFolder, "MetricBot.exe");

            if (!File.Exists(executable))
            {
                ShowError(
                    $"Не найден файл приложения:\n{executable}\n\n" +
                    "Распакуйте комплект MetricBot полностью, сохранив папки Legacy и Modern.");
                return 2;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
            };

            foreach (var argument in args)
                startInfo.ArgumentList.Add(argument);

            Process.Start(startInfo);
            return 0;
        }
        catch (Win32Exception ex)
        {
            ShowError($"Не удалось запустить MetricBot.\n\n{ex.Message}");
            return ex.NativeErrorCode;
        }
        catch (Exception ex)
        {
            ShowError($"Не удалось запустить MetricBot.\n\n{ex.Message}");
            return 1;
        }
    }

    private static bool IsLegacyWindows(Version version) =>
        version.Major == 6 && version.Minor is >= 1 and <= 3;

    private static Version GetWindowsVersion()
    {
        var info = new OsVersionInfo
        {
            Size = Marshal.SizeOf<OsVersionInfo>(),
        };

        return RtlGetVersion(ref info) == 0
            ? new Version(info.Major, info.Minor, info.Build)
            : Environment.OSVersion.Version;
    }

    private static void ShowError(string message) =>
        MessageBox(IntPtr.Zero, message, "MetricBot", MbIconError);

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfo versionInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public int Size;
        public int Major;
        public int Minor;
        public int Build;
        public int PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;
    }
}
