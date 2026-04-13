using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DropCast
{
    public class ChannelHistoryEntry
    {
        public ulong ServerId { get; set; }
        public string ServerName { get; set; }
        public ulong ChannelId { get; set; }
        public string ChannelName { get; set; }
    }

    public class UserSettings
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropCast");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        public int Volume { get; set; } = 100;
        public bool ClickToDismissEnabled { get; set; }
        public bool ShowAuthorInfoEnabled { get; set; }
        public ulong ServerId { get; set; }
        public ulong ChannelId { get; set; }
        public List<ChannelHistoryEntry> ChannelHistory { get; set; } = new List<ChannelHistoryEntry>();

        public void AddToHistory(ulong serverId, string serverName, ulong channelId, string channelName)
        {
            ChannelHistory.RemoveAll(h => h.ChannelId == channelId);
            ChannelHistory.Insert(0, new ChannelHistoryEntry
            {
                ServerId = serverId,
                ServerName = serverName,
                ChannelId = channelId,
                ChannelName = channelName
            });
            if (ChannelHistory.Count > 10)
                ChannelHistory = ChannelHistory.Take(10).ToList();
        }

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
