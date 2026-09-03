using System.IO;
using System.Text.Json;

namespace MetricBot
{
    public enum VisitMode
    {
        All,        // открывать все ссылки за обход
        Sequential, // по очереди — одну ссылку за обход
        Random,     // случайную ссылку за обход
    }


    public class Config
    {
        public List<string> Urls         { get; set; } = [];
        public int    MinInterval        { get; set; } = 31;
        public int    MaxInterval        { get; set; } = 39;
        public bool   StartMinimized     { get; set; } = false;
        public bool   Autostart          { get; set; } = false;
        public VisitMode   VisitMode     { get; set; } = VisitMode.All;
        public int    MaxLogLines        { get; set; } = 500;
        public string LogPath            { get; set; } = "";   // "" = %LocalAppData%\MetricBot\Logs
    }

    public static class AppConfig
    {
        private static readonly string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MetricBot");

        private static readonly string _path = Path.Combine(_directory, "config.json");
        private static readonly string _legacyPath =
            Path.Combine(AppContext.BaseDirectory, "config.json");

        public static Config Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    Current = Read(_path);
                    return;
                }

                if (!File.Exists(_legacyPath))
                    return;

                Current = Read(_legacyPath);
            }
            catch
            {
                Current = new Config();
                return;
            }

            // Миграция со старого расположения. Исходный файл намеренно не удаляется.
            try { Save(); }
            catch { /* Настройки уже загружены и будут доступны в текущем сеансе. */ }
        }

        public static void Save()
        {
            var json = JsonSerializer.Serialize(Current,
                new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_path, json);
        }

        private static Config Read(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
    }
}
