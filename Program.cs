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
        private static string ConfigFilePath = "config.txt";
        private static ServiceProvider _services;
        private static ulong _channelId;
        private static Form1 _desktopWindow;
        private static string BotToken;

        private static MessagePipeline _pipeline;
        private static DiscordMessageSource _discordSource;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _channelId = LoadChannelId();
            BotToken = LoadBotToken();

            Form1 desktopWindow = new Form1();

            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "log.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, logconsole);
            config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            desktopWindow.Load += async (sender, e) => await StartPipeline(desktopWindow);
            desktopWindow.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.F10)
                {
                    OpenChannelInputForm();
                }
            };

            Application.Run(desktopWindow);
        }

        /// <summary>
        /// Initializes the multi-source message pipeline.
        /// Discord is the first source; add WhatsApp, Telegram, REST API, etc. here.
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
            _discordSource = new DiscordMessageSource(BotToken, _channelId, logger);

            _pipeline = _services.GetService<MessagePipeline>();
            _pipeline.RegisterSource(_discordSource);

            var localDropSource = new LocalDropMessageSource();
            _pipeline.RegisterSource(localDropSource);

            // TODO: Register additional sources here:
            // _pipeline.RegisterSource(new WhatsAppMessageSource(...));
            // _pipeline.RegisterSource(new TelegramMessageSource(...));

            await _pipeline.ConnectAllAsync();
        }

        static void ChangeDiscordChannel(ulong newChannelId)
        {
            _channelId = newChannelId;
            SaveChannelId(newChannelId);
            _discordSource?.SetChannelId(newChannelId);
        }

        static void OpenChannelInputForm()
        {
            Form inputForm = new Form
            {
                Text = "Entrer l'ID du canal Discord",
                Width = 300,
                Height = 150,
                StartPosition = FormStartPosition.CenterScreen
            };

            TextBox inputBox = new TextBox
            {
                Left = 20,
                Top = 20,
                Width = 240,
                Text = _channelId.ToString()
            };

            Button confirmButton = new Button
            {
                Text = "Valider",
                Left = 100,
                Top = 60,
                Width = 100
            };

            confirmButton.Click += (sender, e) =>
            {
                if (ulong.TryParse(inputBox.Text, out ulong newChannelId))
                {
                    ChangeDiscordChannel(newChannelId);
                    inputForm.Close();
                }
                else
                {
                    MessageBox.Show("Veuillez entrer un ID valide.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            inputForm.Controls.Add(inputBox);
            inputForm.Controls.Add(confirmButton);
            inputForm.ShowDialog();
        }

        private static void SaveChannelId(ulong channelId)
        {
            File.WriteAllText(ConfigFilePath, channelId.ToString());
        }

        private static ulong LoadChannelId()
        {
            if (File.Exists(ConfigFilePath))
            {
                string content = File.ReadAllText(ConfigFilePath);
                if (ulong.TryParse(content, out ulong savedChannelId))
                {
                    return savedChannelId;
                }
            }
            return 1346607416229236749; // ID par défaut
        }

        private static string LoadBotToken()
        {
            string tokenPath = "token.txt";
            if (File.Exists(tokenPath))
            {
                return File.ReadAllText(tokenPath).Trim();
            }
            throw new FileNotFoundException("Le fichier token.txt est introuvable. Créez-le à la racine du projet avec votre token Discord.");
        }
    }
}