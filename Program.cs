using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NLog;
using NLog.Extensions.Logging;
using Discord.WebSocket;
using Discord;
using System.IO;
using System.Text;
using DropCast.Abstractions;
using DropCast.Services;
using DropCast.Sources;

namespace DropCast
{
    internal static class Program
    {
        private static ServiceProvider _services;
        private static UserSettings _settings;
        private static Form1 _desktopWindow;
        private static string BotToken;

        private static MessagePipeline _pipeline;
        private static DiscordMessageSource _discordSource;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _settings = UserSettings.Load();
            MigrateConfigTxt();
            BotToken = LoadBotToken();

            Form1 desktopWindow = new Form1();

            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "log.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, logconsole);
            config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            desktopWindow.Load += async (sender, e) => await StartPipeline(desktopWindow);
            desktopWindow.ChangeChannelRequested += (sender, e) => OpenChannelPickerForm();

            Application.Run(desktopWindow);
        }

        /// <summary>
        /// Initializes the multi-source message pipeline.
        /// </summary>
        static async Task StartPipeline(Form1 desktopWindow)
        {
            _desktopWindow = desktopWindow;

            _services = new ServiceCollection()
                .AddOptions()
                .Configure<DiscordServiceOptions>(options =>
                {
                    options.BotToken = BotToken;
                })
                .AddSingleton<VideoResolver>()
                .AddSingleton<VideoTrimmer>()
                .AddSingleton<IMediaDisplay>(desktopWindow)
                .AddSingleton<MessagePipeline>()
                .AddLogging(logging =>
                {
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    logging.AddNLog(new NLogProviderOptions
                    {
                        CaptureMessageTemplates = true,
                        CaptureMessageProperties = true
                    });
                })
                .BuildServiceProvider();

            var logger = _services.GetService<ILogger<DiscordMessageSource>>();
            _discordSource = new DiscordMessageSource(BotToken, _settings.ChannelId, logger);

            _pipeline = _services.GetService<MessagePipeline>();
            _pipeline.RegisterSource(_discordSource);

            var localDropSource = new LocalDropMessageSource(_discordSource);
            _pipeline.RegisterSource(localDropSource);

            await _pipeline.ConnectAllAsync();

            // Show current channel in the control panel
            var currentEntry = _settings.ChannelHistory.Find(h => h.ChannelId == _settings.ChannelId);
            if (currentEntry != null)
                _desktopWindow?.UpdateChannelInfo(currentEntry.ServerName, currentEntry.ChannelName);
        }

        static void OpenChannelPickerForm()
        {
            if (_discordSource == null) return;

            using (var picker = new ChannelPickerForm(_discordSource, _settings.ServerId, _settings.ChannelId, _settings.ChannelHistory, _settings.KnownServerIds))
            {
                var result = picker.ShowDialog();

                // Save known servers even if the user cancelled (they may have added a server via invite)
                _settings.Save();

                if (result == DialogResult.OK)
                {
                    _settings.ServerId = picker.SelectedServerId;
                    _settings.ChannelId = picker.SelectedChannelId;
                    _settings.AddKnownServer(picker.SelectedServerId);
                    _settings.AddToHistory(picker.SelectedServerId, picker.SelectedServerName,
                        picker.SelectedChannelId, picker.SelectedChannelName);
                    _settings.Save();
                    _discordSource.SetChannelId(picker.SelectedChannelId);
                    _desktopWindow?.UpdateChannelInfo(picker.SelectedServerName, picker.SelectedChannelName);
                }
            }
        }

        /// <summary>
        /// One-time migration from legacy config.txt to UserSettings.
        /// </summary>
        private static void MigrateConfigTxt()
        {
            string configPath = "config.txt";
            if (_settings.ChannelId == 0 && File.Exists(configPath))
            {
                string content = File.ReadAllText(configPath).Trim();
                if (ulong.TryParse(content, out ulong channelId))
                {
                    _settings.ChannelId = channelId;
                    _settings.Save();
                }
                File.Delete(configPath);
            }
        }

        private static string LoadBotToken()
        {
            string encPath = "token.enc";

            // Try encrypted token first
            string token = Services.TokenProvider.LoadEncrypted(encPath);
            if (token != null) return token;

            // Migrate from legacy plain-text token.txt
            string plainPath = "token.txt";
            if (File.Exists(plainPath))
            {
                token = File.ReadAllText(plainPath).Trim();
                Services.TokenProvider.SaveEncrypted(token, encPath);
                File.Delete(plainPath);
                return token;
            }

            // Clean up corrupted token.enc if present
            if (File.Exists(encPath)) File.Delete(encPath);

            throw new FileNotFoundException(
                "Le fichier token.enc est introuvable ou corrompu. " +
                "Placez un fichier token.txt à la racine du projet ; il sera automatiquement chiffré au premier lancement.");
        }
    }
}