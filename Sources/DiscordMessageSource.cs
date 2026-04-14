using Discord;
using Discord.WebSocket;
using DropCast.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DropCast.Sources
{
    /// <summary>
    /// Discord implementation of <see cref="IMessageSource"/>.
    /// Listens to a specific channel and converts Discord messages to DropCastMessages.
    /// </summary>
    public class DiscordMessageSource : IMessageSource
    {
        private readonly ILogger _logger;
        private readonly DiscordSocketClient _client;
        private readonly string _botToken;
        private ulong _channelId;
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();

        private static readonly Dictionary<string, MediaType> ExtensionToMediaType = new Dictionary<string, MediaType>(StringComparer.OrdinalIgnoreCase)
{
    // Video
    { ".mp4", MediaType.Video },
    { ".m4v", MediaType.Video },
    { ".mov", MediaType.Video },
    { ".avi", MediaType.Video },
    { ".mkv", MediaType.Video },
    { ".webm", MediaType.Video },
    { ".wmv", MediaType.Video },
    { ".flv", MediaType.Video },
    { ".f4v", MediaType.Video },
    { ".mpeg", MediaType.Video },
    { ".mpg", MediaType.Video },
    { ".mpe", MediaType.Video },
    { ".m1v", MediaType.Video },
    { ".m2v", MediaType.Video },
    { ".ts", MediaType.Video },
    { ".mts", MediaType.Video },
    { ".m2ts", MediaType.Video },
    { ".vob", MediaType.Video },
    { ".3gp", MediaType.Video },
    { ".3g2", MediaType.Video },
    { ".ogv", MediaType.Video },
    { ".ogm", MediaType.Video },
    { ".divx", MediaType.Video },
    { ".asf", MediaType.Video },
    { ".rm", MediaType.Video },
    { ".rmvb", MediaType.Video },
    { ".dv", MediaType.Video },
    { ".mxf", MediaType.Video },
    { ".nut", MediaType.Video },
    { ".nsv", MediaType.Video },
    { ".amv", MediaType.Video },
    { ".drc", MediaType.Video },
    { ".gxf", MediaType.Video },
    { ".roq", MediaType.Video },
    { ".ivf", MediaType.Video },
    { ".fli", MediaType.Video },
    { ".flc", MediaType.Video },
    { ".yuv", MediaType.Video },

    // Audio
    { ".mp3", MediaType.Audio },
    { ".wav", MediaType.Audio },
    { ".ogg", MediaType.Audio },
    { ".flac", MediaType.Audio },
    { ".aac", MediaType.Audio },
    { ".wma", MediaType.Audio },
    { ".m4a", MediaType.Audio },
    { ".opus", MediaType.Audio },
    { ".aiff", MediaType.Audio },
    { ".alac", MediaType.Audio },

    // Image
    { ".jpg", MediaType.Image },
    { ".jpeg", MediaType.Image },
    { ".png", MediaType.Image },
    { ".gif", MediaType.Image },
    { ".bmp", MediaType.Image },
    { ".webp", MediaType.Image },
    { ".tiff", MediaType.Image },
    { ".tif", MediaType.Image },
    { ".svg", MediaType.Image },
    { ".ico", MediaType.Image },
    { ".heic", MediaType.Image },
    { ".heif", MediaType.Image },
    { ".avif", MediaType.Image },
};

        public string PlatformName => "Discord";
        public event EventHandler<DropCastMessage> MessageReceived;

        private readonly object _selfPostedLock = new object();
        private readonly HashSet<ulong> _selfPostedIds = new HashSet<ulong>();
        private int _pendingUploads;

        public DiscordMessageSource(string botToken, ulong channelId, ILogger logger)
        {
            _botToken = botToken;
            _channelId = channelId;
            _logger = logger;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            });
            _client.Log += msg => { _logger.LogInformation(msg.ToString()); return Task.CompletedTask; };
            _client.Ready += () => { _ready.TrySetResult(true); return Task.CompletedTask; };
            _client.MessageReceived += OnDiscordMessage;
        }

        public async Task ConnectAsync()
        {
            if (_client.LoginState == LoginState.LoggedIn) return;
            await _client.LoginAsync(TokenType.Bot, _botToken);
            await _client.StartAsync();
            await _ready.Task;
        }

        public async Task DisconnectAsync()
        {
            await _client.LogoutAsync();
            await _client.StopAsync();
        }

        public void SetChannelId(ulong channelId)
        {
            _channelId = channelId;
            _logger.LogInformation("🔄 Discord channel changed to {ChannelId}", channelId);
        }

        public SocketTextChannel GetSocketTextChannel(ulong channelId) =>
            _client.GetChannel(channelId) as SocketTextChannel;

        public List<(ulong Id, string Name)> GetGuilds()
        {
            return _client.Guilds
                .OrderBy(g => g.Name)
                .Select(g => (g.Id, g.Name))
                .ToList();
        }

        public List<(ulong Id, string Name)> GetTextChannels(ulong guildId)
        {
            var guild = _client.GetGuild(guildId);
            if (guild == null) return new List<(ulong, string)>();
            return guild.TextChannels
                .OrderBy(c => c.Position)
                .Select(c => (c.Id, c.Name))
                .ToList();
        }

        public ulong BotUserId => _client.CurrentUser?.Id ?? 0;

        public bool IsInGuild(ulong guildId) => _client.GetGuild(guildId) != null;

        public async Task<InviteInfo> ResolveInviteAsync(string inviteCode)
        {
            try
            {
                var invite = await _client.GetInviteAsync(inviteCode);
                if (invite?.GuildId == null) return null;
                return new InviteInfo { GuildId = invite.GuildId.Value, GuildName = invite.GuildName };
            }
            catch
            {
                return null;
            }
        }

        public string GetBotInviteUrl(ulong guildId)
        {
            ulong botId = _client.CurrentUser.Id;
            return string.Format(
                "https://discord.com/oauth2/authorize?client_id={0}&scope=bot&permissions=66560&guild_id={1}",
                botId, guildId);
        }

        /// <summary>
        /// Uploads a local file to the currently selected Discord channel.
        /// The resulting message ID is tracked so the local gateway echo is skipped.
        /// </summary>
        public async Task UploadFileAsync(string filePath, string caption)
        {
            var channel = _client.GetChannel(_channelId) as IMessageChannel;
            if (channel == null)
            {
                _logger.LogWarning("Cannot upload file: channel {ChannelId} not found or not accessible", _channelId);
                return;
            }

            Interlocked.Increment(ref _pendingUploads);
            try
            {
                var msg = await channel.SendFileAsync(filePath, text: string.IsNullOrWhiteSpace(caption) ? null : caption);
                lock (_selfPostedLock)
                    _selfPostedIds.Add(msg.Id);
                _logger.LogInformation("📤 Uploaded {File} to channel {Channel} (msg {Id})",
                    System.IO.Path.GetFileName(filePath), _channelId, msg.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload {File} to Discord", System.IO.Path.GetFileName(filePath));
            }
            finally
            {
                Interlocked.Decrement(ref _pendingUploads);
            }
        }

        public static string ParseInviteCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim();
            if (input.Contains("discord.gg/"))
                input = input.Substring(input.LastIndexOf("discord.gg/") + "discord.gg/".Length);
            else if (input.Contains("discord.com/invite/"))
                input = input.Substring(input.LastIndexOf("discord.com/invite/") + "discord.com/invite/".Length);
            int q = input.IndexOf('?');
            if (q >= 0) input = input.Substring(0, q);
            int s = input.IndexOf('/');
            if (s >= 0) input = input.Substring(0, s);
            return input;
        }

        public class InviteInfo
        {
            public ulong GuildId { get; set; }
            public string GuildName { get; set; }
        }

        private Task OnDiscordMessage(SocketMessage rawMessage)
        {
            if (rawMessage.Channel.Id != _channelId) return Task.CompletedTask;
            if (!(rawMessage is SocketUserMessage userMessage)) return Task.CompletedTask;

            // Messages from our own bot: skip echo of uploads we sent, process others
            if (rawMessage.Author.Id == _client.CurrentUser?.Id)
            {
                lock (_selfPostedLock)
                {
                    if (_selfPostedIds.Remove(rawMessage.Id))
                        return Task.CompletedTask;
                }
                // Gateway can fire before SendFileAsync returns the ID — if we have
                // pending uploads this message is almost certainly the echo.
                if (_pendingUploads > 0)
                    return Task.CompletedTask;
            }
            else if (userMessage.Author.IsBot)
            {
                return Task.CompletedTask;
            }

            var attachments = new List<MediaContent>();
            foreach (var att in rawMessage.Attachments)
            {
                string extension = System.IO.Path.GetExtension(att.Filename);
                if (string.IsNullOrEmpty(extension) || !ExtensionToMediaType.TryGetValue(extension, out MediaType type))
                    continue;

                attachments.Add(new MediaContent
                {
                    Type = type,
                    Url = att.Url,
                    FileName = att.Filename
                });
            }

            var msg = new DropCastMessage
            {
                // Content = raw text from Discord API (preserves underscores in URLs)
                // CleanContent resolves @mentions but can strip _ as markdown italic markers
                Text = rawMessage.Content,
                Caption = rawMessage.Content,
                AuthorName = rawMessage.Author.Username,
                AuthorAvatarUrl = rawMessage.Author.GetAvatarUrl(ImageFormat.Auto, 64)
                                  ?? rawMessage.Author.GetDefaultAvatarUrl(),
                SourcePlatform = PlatformName,
                Attachments = attachments.ToArray()
            };

            _logger.LogInformation("📩 [{Platform}] {Author}: {Text} ({Count} attachments)",
                PlatformName, msg.AuthorName, msg.Text, msg.Attachments.Length);

            MessageReceived?.Invoke(this, msg);
            return Task.CompletedTask;
        }
    }
}
