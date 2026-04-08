using Discord;
using Discord.WebSocket;
using DropCast.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private Task OnDiscordMessage(SocketMessage rawMessage)
        {
            if (rawMessage.Channel.Id != _channelId) return Task.CompletedTask;
            if (!(rawMessage is SocketUserMessage userMessage)) return Task.CompletedTask;
            if (userMessage.Author.IsBot) return Task.CompletedTask;

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
