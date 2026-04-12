using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace InstantTraceViewerUI
{
    internal enum FontType
    {
        SegoeUI,
        DroidSans,
        CascadiaMono,
        ProggyClean
    }

    internal static class Settings
    {
        private sealed class SettingsStore
        {
            public string? Theme { get; set; }
            public string? Font { get; set; }
            public int? FontSize { get; set; }
            public string? WprpOpenLocation { get; set; }
            public string? CsvOpenLocation { get; set; }
            public string? TsvOpenLocation { get; set; }
            public string? PerfettoOpenLocation { get; set; }
            public string? EtlOpenLocation { get; set; }
            public string? ItvfLocation { get; set; }
            public List<string>? RecentlyOpenedWprp { get; set; }
            public List<string>? RecentlyOpenedItvf { get; set; }
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InstantTraceViewer");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

        private static readonly object Sync = new();
        private static readonly SettingsStore Store = LoadStore();

        private static FontType _cachedFont;
        private static int _cachedFontSize;
        private static ImGuiTheme _imguiTheme;

        static Settings()
        {
            _cachedFont = Enum.TryParse(Store.Font, out FontType font) ? font : FontType.SegoeUI;
            _cachedFontSize = Store.FontSize ?? 17;
            _imguiTheme = Enum.TryParse(Store.Theme, out ImGuiTheme theme) ? theme : ImGuiTheme.Light;
        }

        public static ImGuiTheme Theme
        {
            get => _imguiTheme;
            set
            {
                _imguiTheme = value;
                Store.Theme = value.ToString();
                SaveStore();
            }
        }

        public static FontType Font
        {
            get => _cachedFont;
            set
            {
                _cachedFont = value;
                Store.Font = value.ToString();
                SaveStore();
            }
        }

        public static int FontSize
        {
            get => _cachedFontSize;
            set
            {
                _cachedFontSize = value;
                Store.FontSize = value;
                SaveStore();
            }
        }

        public static string? WprpOpenLocation
        {
            get => Store.WprpOpenLocation;
            set
            {
                Store.WprpOpenLocation = value;
                SaveStore();
            }
        }

        public static string? CsvOpenLocation
        {
            get => Store.CsvOpenLocation;
            set
            {
                Store.CsvOpenLocation = value;
                SaveStore();
            }
        }

        public static string? TsvOpenLocation
        {
            get => Store.TsvOpenLocation;
            set
            {
                Store.TsvOpenLocation = value;
                SaveStore();
            }
        }

        public static string? PerfettoOpenLocation
        {
            get => Store.PerfettoOpenLocation;
            set
            {
                Store.PerfettoOpenLocation = value;
                SaveStore();
            }
        }

        public static string? EtlOpenLocation
        {
            get => Store.EtlOpenLocation;
            set
            {
                Store.EtlOpenLocation = value;
                SaveStore();
            }
        }

        public static string? InstantTraceViewerFiltersLocation
        {
            get
            {
                string? location = Store.ItvfLocation;
                if (string.IsNullOrEmpty(location))
                {
                    location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Instant Trace Viewer Filters");
                    try
                    {
                        if (!Directory.Exists(location))
                        {
                            Directory.CreateDirectory(location);
                        }
                    }
                    catch
                    {
                    }
                }

                return location;
            }
            set
            {
                Store.ItvfLocation = value;
                SaveStore();
            }
        }

        public static void AddRecentlyOpenedWprp(string file)
        {
            AddMru(Store.RecentlyOpenedWprp ??= new List<string>(), file);
        }

        public static IReadOnlyList<string> GetRecentlyOpenedWprp()
        {
            return GetExistingFiles(Store.RecentlyOpenedWprp);
        }

        public static void AddRecentlyUsedItvf(string file)
        {
            AddMru(Store.RecentlyOpenedItvf ??= new List<string>(), file);
        }

        public static IReadOnlyList<string> GetRecentlyOpenedItfv()
        {
            return GetExistingFiles(Store.RecentlyOpenedItvf);
        }

        public static void AssociateWithEtlExtensions()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("ETL file association is only supported on Windows.");
            }

            string exePath = Path.Combine(AppContext.BaseDirectory, "InstantTraceViewerUI.exe");

            using var progIdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\InstantTraceViewerUI.etl");
            using var iconKey = progIdKey!.CreateSubKey("DefaultIcon");
            using var openKey = progIdKey.CreateSubKey("shell\\open");
            using var commandKey = progIdKey.CreateSubKey("shell\\open\\command");

            progIdKey.SetValue("", "ETL Trace file");
            iconKey!.SetValue("", Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.ico"));
            openKey!.SetValue("Icon", $"\"{exePath}\"");
            commandKey!.SetValue("", $"\"{exePath}\" \"%1\"");

            foreach (string ext in Etw.EtwTraceSource.EtlFileExtensions)
            {
                using var openWithKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                openWithKey!.SetValue("", "InstantTraceViewerUI.etl");
                openWithKey.SetValue("InstantTraceViewerUI.etl", "");
            }
        }

        private static IReadOnlyList<string> GetExistingFiles(List<string>? values)
        {
            return (values ?? new List<string>()).Where(File.Exists).ToList();
        }

        private static void AddMru(List<string> items, string path)
        {
            items.Insert(0, path);

            List<string> deduped = new();
            foreach (string item in items)
            {
                if (!deduped.Contains(item, StringComparer.Ordinal))
                {
                    deduped.Add(item);
                }
            }

            if (deduped.Count > 10)
            {
                deduped.RemoveRange(10, deduped.Count - 10);
            }

            items.Clear();
            items.AddRange(deduped);
            SaveStore();
        }

        private static SettingsStore LoadStore()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    SettingsStore? store = JsonSerializer.Deserialize<SettingsStore>(json);
                    if (store != null)
                    {
                        return store;
                    }
                }
            }
            catch
            {
            }

            return new SettingsStore();
        }

        private static void SaveStore()
        {
            lock (Sync)
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(Store, SerializerOptions);
                File.WriteAllText(SettingsPath, json);
            }
        }
    }
}
