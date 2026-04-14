using System.Text.Json;

namespace DropCast.Android.Services;

public record ChannelHistoryEntry(ulong ServerId, string ServerName, ulong ChannelId, string ChannelName);

public static class AppSettings
{
    private const string ChannelKey = "discord_channel_id";
    private const string HistoryKey = "channel_history_json";
    private const string KnownServersKey = "known_server_ids_json";

    public static ulong ChannelId
    {
        get
        {
            var str = Preferences.Default.Get(ChannelKey, "");
            return ulong.TryParse(str, out var id) ? id : 0;
        }
        set => Preferences.Default.Set(ChannelKey, value.ToString());
    }

    public static ulong ServerId
    {
        get
        {
            var str = Preferences.Default.Get("discord_server_id", "");
            return ulong.TryParse(str, out var id) ? id : 0;
        }
        set => Preferences.Default.Set("discord_server_id", value.ToString());
    }

    // Overlay zone (stored as fractions 0.0–1.0 of screen size)
    public static float OverlayZoneLeft
    {
        get => Preferences.Default.Get("overlay_zone_left", 0f);
        set => Preferences.Default.Set("overlay_zone_left", value);
    }

    public static float OverlayZoneTop
    {
        get => Preferences.Default.Get("overlay_zone_top", 0f);
        set => Preferences.Default.Set("overlay_zone_top", value);
    }

    public static float OverlayZoneWidth
    {
        get => Preferences.Default.Get("overlay_zone_width", 1f);
        set => Preferences.Default.Set("overlay_zone_width", value);
    }

    public static float OverlayZoneHeight
    {
        get => Preferences.Default.Get("overlay_zone_height", 1f);
        set => Preferences.Default.Set("overlay_zone_height", value);
    }

    /// <summary>Background opacity of the overlay (0 = fully transparent, 100 = fully opaque). Default 80.</summary>
    public static int OverlayBackgroundOpacity
    {
        get => Preferences.Default.Get("overlay_bg_opacity", 80);
        set => Preferences.Default.Set("overlay_bg_opacity", Math.Clamp(value, 0, 100));
    }

    public static List<ChannelHistoryEntry> GetChannelHistory()
    {
        string json = Preferences.Default.Get(HistoryKey, "");
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<ChannelHistoryEntry>>(json) ?? []; }
        catch { return []; }
    }

    public static void AddToChannelHistory(ulong serverId, string serverName, ulong channelId, string channelName)
    {
        var history = GetChannelHistory();
        history.RemoveAll(h => h.ChannelId == channelId);
        history.Insert(0, new ChannelHistoryEntry(serverId, serverName, channelId, channelName));
        if (history.Count > 10) history = history.GetRange(0, 10);
        Preferences.Default.Set(HistoryKey, JsonSerializer.Serialize(history));
    }

    public static List<ulong> GetKnownServerIds()
    {
        string json = Preferences.Default.Get(KnownServersKey, "");
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<ulong>>(json) ?? []; }
        catch { return []; }
    }

    public static void AddKnownServer(ulong serverId)
    {
        if (serverId == 0) return;
        var ids = GetKnownServerIds();
        if (!ids.Contains(serverId))
        {
            ids.Add(serverId);
            Preferences.Default.Set(KnownServersKey, JsonSerializer.Serialize(ids));
        }
    }

    /// <summary>
    /// One-time migration: populate KnownServerIds from existing history/selection for users updating from older versions.
    /// </summary>
    public static void MigrateKnownServers()
    {
        if (GetKnownServerIds().Count > 0) return;

        var ids = new List<ulong>();
        if (ServerId != 0)
            ids.Add(ServerId);

        foreach (var entry in GetChannelHistory())
        {
            if (!ids.Contains(entry.ServerId))
                ids.Add(entry.ServerId);
        }

        if (ids.Count > 0)
            Preferences.Default.Set(KnownServersKey, JsonSerializer.Serialize(ids));
    }
}
