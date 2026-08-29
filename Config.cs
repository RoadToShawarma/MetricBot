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
        public string LogPath            { get; set; } = "";   // "" = рядом с exe
    }

    public static class AppConfig
    {
        private static readonly string _path =
            Path.Combine(AppContext.BaseDirectory, "config.json");

        public static Config Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    Current  = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
            }
            catch { Current = new Config(); }
        }

        public static void Save()
        {
            var json = JsonSerializer.Serialize(Current,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}
