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
        public List<ulong> KnownServerIds { get; set; } = new List<ulong>();

        public void AddKnownServer(ulong serverId)
        {
            if (serverId != 0 && !KnownServerIds.Contains(serverId))
                KnownServerIds.Add(serverId);
        }

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
            UserSettings settings;
            try
            {
                if (File.Exists(SettingsPath))
                    settings = JsonConvert.DeserializeObject<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
                else
                    settings = new UserSettings();
            }
            catch { settings = new UserSettings(); }

            // Migration: populate KnownServerIds from existing history/selection
            if (settings.KnownServerIds.Count == 0 && (settings.ServerId != 0 || settings.ChannelHistory.Count > 0))
            {
                if (settings.ServerId != 0)
                    settings.KnownServerIds.Add(settings.ServerId);
                foreach (var entry in settings.ChannelHistory)
                {
                    if (!settings.KnownServerIds.Contains(entry.ServerId))
                        settings.KnownServerIds.Add(entry.ServerId);
                }
                settings.Save();
            }

            return settings;
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
