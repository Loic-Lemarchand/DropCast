using Newtonsoft.Json;
using System;
using System.IO;

namespace DropCast
{
    public class UserSettings
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropCast");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        public int Volume { get; set; } = 100;
        public bool ClickToDismissEnabled { get; set; }
        public bool ShowAuthorInfoEnabled { get; set; }

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonConvert.DeserializeObject<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
            }
            catch { }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch { }
        }
    }
}
