using Discord;
using Discord.WebSocket;
using DropCast.Android.Models;
using Microsoft.Extensions.Logging;

namespace DropCast.Android.Services;

public class DiscordService
{
    private readonly ILogger<DiscordService> _logger;
    private DiscordSocketClient? _client;
    private string _botToken = "";
    private ulong _channelId;
    private TaskCompletionSource<bool>? _ready;

    private static readonly Dictionary<string, MediaType> ExtensionToMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".mp4", MediaType.Video }, { ".mov", MediaType.Video }, { ".avi", MediaType.Video },
        { ".mkv", MediaType.Video }, { ".webm", MediaType.Video }, { ".wmv", MediaType.Video },
        { ".flv", MediaType.Video }, { ".3gp", MediaType.Video }, { ".m4v", MediaType.Video },
        { ".ts", MediaType.Video },
        { ".mp3", MediaType.Audio }, { ".wav", MediaType.Audio }, { ".ogg", MediaType.Audio },
        { ".flac", MediaType.Audio }, { ".aac", MediaType.Audio }, { ".m4a", MediaType.Audio },
        { ".opus", MediaType.Audio },
        { ".jpg", MediaType.Image }, { ".jpeg", MediaType.Image }, { ".png", MediaType.Image },
        { ".gif", MediaType.Image }, { ".bmp", MediaType.Image }, { ".webp", MediaType.Image },
        { ".heic", MediaType.Image }, { ".avif", MediaType.Image },
    };

    public event EventHandler<DropCastMessage>? MessageReceived;
    public bool IsConnected => _client?.ConnectionState == ConnectionState.Connected;

    public ulong ChannelId
    {
        get => _channelId;
        set
        {
            _channelId = value;
            _logger.LogInformation("📡 Discord channel set to {Id}", value);
        }
    }

    public DiscordService(ILogger<DiscordService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connect the bot without selecting a channel yet.
    /// After this completes, call GetGuilds / GetTextChannels to let the user pick.
    /// </summary>
    public async Task ConnectAsync(string botToken)
    {
        _botToken = botToken;

        if (_client != null)
        {
            _client.MessageReceived -= OnDiscordMessage;
            try { await _client.StopAsync(); } catch { }
            _client.Dispose();
        }

        _ready = new TaskCompletionSource<bool>();
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });

        _client.Log += msg => { _logger.LogInformation("{Msg}", msg.ToString()); return Task.CompletedTask; };
        _client.Ready += () => { _ready.TrySetResult(true); return Task.CompletedTask; };
        _client.MessageReceived += OnDiscordMessage;

        await _client.LoginAsync(TokenType.Bot, _botToken);
        await _client.StartAsync();

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        if (await Task.WhenAny(_ready.Task, timeoutTask) == timeoutTask)
            throw new TimeoutException("Discord connection timed out");

        _logger.LogInformation("🟢 Discord bot connected ({Guilds} servers)", _client.Guilds.Count);
    }

    /// <summary>Connect and immediately start listening on a specific channel.</summary>
    public async Task ConnectAsync(string botToken, ulong channelId)
    {
        await ConnectAsync(botToken);
        ChannelId = channelId;
    }

    public record DiscordGuildInfo(ulong Id, string Name);
    public record DiscordChannelInfo(ulong Id, string Name);

    public List<DiscordGuildInfo> GetGuilds()
    {
        if (_client == null) return [];
        return _client.Guilds
            .OrderBy(g => g.Name)
            .Select(g => new DiscordGuildInfo(g.Id, g.Name))
            .ToList();
    }

    public List<DiscordChannelInfo> GetTextChannels(ulong guildId)
    {
        var guild = _client?.GetGuild(guildId);
        if (guild == null) return [];
        return guild.TextChannels
            .OrderBy(c => c.Position)
            .Select(c => new DiscordChannelInfo(c.Id, c.Name))
            .ToList();
    }

    public ulong BotUserId => _client?.CurrentUser?.Id ?? 0;

    public bool IsInGuild(ulong guildId) => _client?.GetGuild(guildId) != null;

    public async Task<(ulong GuildId, string GuildName)?> ResolveInviteAsync(string inviteCode)
    {
        if (_client == null) return null;
        try
        {
            var invite = await _client.GetInviteAsync(inviteCode);
            if (invite?.GuildId == null) return null;
            return (invite.GuildId.Value, invite.GuildName);
        }
        catch { return null; }
    }

    public string GetBotInviteUrl(ulong guildId)
    {
        ulong botId = _client?.CurrentUser?.Id ?? 0;
        return $"https://discord.com/oauth2/authorize?client_id={botId}&scope=bot&permissions=66560&guild_id={guildId}";
    }

    public static string ParseInviteCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.Trim();
        if (input.Contains("discord.gg/"))
            input = input[(input.LastIndexOf("discord.gg/") + "discord.gg/".Length)..];
        else if (input.Contains("discord.com/invite/"))
            input = input[(input.LastIndexOf("discord.com/invite/") + "discord.com/invite/".Length)..];
        int q = input.IndexOf('?');
        if (q >= 0) input = input[..q];
        int s = input.IndexOf('/');
        if (s >= 0) input = input[..s];
        return input;
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            _client.MessageReceived -= OnDiscordMessage;
            await _client.LogoutAsync();
            await _client.StopAsync();
            _client.Dispose();
            _client = null;
        }
    }

    private Task OnDiscordMessage(SocketMessage rawMessage)
    {
        if (rawMessage.Channel.Id != _channelId) return Task.CompletedTask;
        if (rawMessage is not SocketUserMessage userMessage) return Task.CompletedTask;
        // Allow messages from our own bot (e.g. drag-and-drop uploads from the desktop app)
        // but ignore other bots.
        if (userMessage.Author.IsBot && rawMessage.Author.Id != (_client?.CurrentUser?.Id ?? 0))
            return Task.CompletedTask;

        var attachments = new List<MediaContent>();
        foreach (var att in rawMessage.Attachments)
        {
            string ext = Path.GetExtension(att.Filename);
            if (string.IsNullOrEmpty(ext) || !ExtensionToMediaType.TryGetValue(ext, out var type))
                continue;
            attachments.Add(new MediaContent { Type = type, Url = att.Url, FileName = att.Filename });
        }

        var msg = new DropCastMessage
        {
            Text = rawMessage.Content,
            Caption = rawMessage.Content,
            AuthorName = rawMessage.Author.Username,
            AuthorAvatarUrl = rawMessage.Author.GetAvatarUrl(Discord.ImageFormat.Auto, 64)
                              ?? rawMessage.Author.GetDefaultAvatarUrl(),
            SourcePlatform = "Discord",
            Attachments = attachments.ToArray()
        };

        _logger.LogInformation("📩 {Author}: {Text} ({N} attachments)", msg.AuthorName, msg.Text, msg.Attachments.Length);
        MessageReceived?.Invoke(this, msg);
        return Task.CompletedTask;
    }
}
