using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

interface IDiscordService
{
    Task<bool> Ready();
    SocketTextChannel GetSocketTextChannel(ulong channelId);
}

public class DiscordServiceOptions
{
    public string BotToken { get; set; }
}

namespace DropCast
{
    public class EventManager : IDiscordService
    {
        private Form1 DesktopWindow;
        private readonly ILogger _logger;
        private readonly DiscordSocketClient _client;
        private readonly string _botToken;
        private TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private ulong _channelId = 1346607416229236749; // ID du canal par défaut

        public EventManager(IOptions<DiscordServiceOptions> options, ILogger<EventManager> logger)
        {
            _logger = logger;
            _botToken = options.Value.BotToken;
            _client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent });
            _client.Log += LogDiscord;
            _client.Ready += OnReady;
            _client.MessageReceived += ReceiveMessage;
            _client.LoginAsync(TokenType.Bot, options.Value.BotToken);
            _client.StartAsync();


        }

        public SocketTextChannel GetSocketTextChannel(ulong channelId) => _client.GetChannel(channelId) as SocketTextChannel;

        public async Task<bool> Ready()
        {
            await _ready.Task;
            return _ready.Task.Result;
        }

        private Task OnReady()
        {
            _ready.SetResult(true);
            return Task.CompletedTask;
        }

        private Task LogDiscord(LogMessage msg)
        {
            _logger.LogInformation(msg.ToString());
            return Task.CompletedTask;
        }

        public async Task ReceiveMessage(SocketMessage rawMessage)
        {
            _logger.LogInformation($"📩 Message reçu : {rawMessage.Content} de {rawMessage.Author.Username} dans #{rawMessage.Channel.Id}");
            if (rawMessage.Channel.Id != _channelId)
            {
                _logger.LogWarning($"🚫 Message ignoré, attendu: {_channelId}, reçu: {rawMessage.Channel.Id}");
                return;
            }
            if (!(rawMessage is SocketUserMessage message))
            {
                _logger.LogWarning("🚫 Message non reconnu comme un utilisateur.");
                return;
            }
            _logger.LogInformation("✅ Message valide traité !");
            if (DesktopWindow == null)
            {
                _logger.LogError("❌ DesktopWindow n'est pas défini ! Assurez-vous qu'il est bien initialisé dans EventManager.");
                return;
            }
            if (DesktopWindow != null)
            {
                bool hasTimedMedia = false;
                if (rawMessage.Attachments.Any())
                {
                    foreach (var attachment in rawMessage.Attachments)
                    {
                        string fileName = attachment.Filename.ToLower();
                        if (fileName.EndsWith(".mp4") || fileName.EndsWith(".mov") || fileName.EndsWith(".avi") || fileName.EndsWith(".mkv"))
                        {
                            _logger.LogInformation($"📹 Vidéo détectée : {attachment.Url}");
                            hasTimedMedia = true;
                        }
                        if (fileName.EndsWith(".mp3") || fileName.EndsWith(".wav") || fileName.EndsWith(".ogg") || fileName.EndsWith(".flac"))
                        {
                            _logger.LogInformation($"🎵 Audio détecté : {attachment.Url}");
                            hasTimedMedia = true;
                        }
                        if (fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg") || fileName.EndsWith(".png") || fileName.EndsWith(".gif"))
                        {
                            _logger.LogInformation($"🖼️ Image détectée : {attachment.Url}");
                            hasTimedMedia = true;
                        }
                    }
                }
                if (!hasTimedMedia)
                {
                    DesktopWindow.DisplayMessage(rawMessage.CleanContent, false);
                }
                if (rawMessage.Attachments.Any())
                {
                    foreach (var attachment in rawMessage.Attachments)
                    {
                        string fileName = attachment.Filename.ToLower();
                        if (fileName.EndsWith(".mp4") || fileName.EndsWith(".mov") || fileName.EndsWith(".avi") || fileName.EndsWith(".mkv"))
                        {
                            DesktopWindow.DisplayVideo(attachment.Url, rawMessage.CleanContent);
                            return;
                        }
                        if (fileName.EndsWith(".mp3") || fileName.EndsWith(".wav") || fileName.EndsWith(".ogg") || fileName.EndsWith(".flac"))
                        {
                            DesktopWindow.PlayAudio(attachment.Url, rawMessage.CleanContent);
                            return;
                        }
                        if (fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg") || fileName.EndsWith(".png") || fileName.EndsWith(".gif"))
                        {
                            _logger.LogInformation($"🖼️ Image détectée : {attachment.Url}");
                            DesktopWindow.DisplayImage(attachment.Url, rawMessage.CleanContent);
                            return;
                        }
                    }
                }
            }
            _logger.LogInformation(rawMessage.CleanContent);
        }

        public void SetDesktopWindow(Form1 desktopWindow)
        {
            this.DesktopWindow = desktopWindow;
        }

        public async Task DisconnectAsync()
        {
            await _client.LogoutAsync();
            await _client.StopAsync();
        }

        public async Task ConnectAsync(string botToken)
        {
            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();
        }

        public void SetChannelId(ulong newChannelId)
        {
            _channelId = newChannelId;
        }

        public async Task InitializeClient()
        {
            if (_client.LoginState == LoginState.LoggedIn) return; 

            await _client.LoginAsync(TokenType.Bot, _botToken);
            await _client.StartAsync();
        }

        public async Task ChangeChannel(ulong newChannelId)
        {
            _channelId = newChannelId;

            if (_client.ConnectionState == ConnectionState.Connected)
            {
                _logger.LogInformation($"🔄 Changement du canal actif : {_channelId}");
            }
            else
            {
                _logger.LogWarning("Le client Discord n'est pas connecté !");
            }
        }


    }
}
