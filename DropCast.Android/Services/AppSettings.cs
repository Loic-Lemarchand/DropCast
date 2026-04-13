namespace DropCast.Android.Services;

public static class AppSettings
{
    private const string TokenKey = "discord_bot_token";
    private const string ChannelKey = "discord_channel_id";
    private const string WhatsAppGroupKey = "whatsapp_group_name";

    public static string BotToken
    {
        get => Preferences.Default.Get(TokenKey, "");
        set => Preferences.Default.Set(TokenKey, value);
    }

    public static ulong ChannelId
    {
        get
        {
            var str = Preferences.Default.Get(ChannelKey, "");
            return ulong.TryParse(str, out var id) ? id : 0;
        }
        set => Preferences.Default.Set(ChannelKey, value.ToString());
    }

    public static string WhatsAppGroupName
    {
        get => Preferences.Default.Get(WhatsAppGroupKey, "");
        set => Preferences.Default.Set(WhatsAppGroupKey, value);
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
}
