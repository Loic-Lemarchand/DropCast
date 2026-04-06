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

namespace DropCast
{
    internal static class Program
    {
        private static string ConfigFilePath = "config.txt";
        private static ServiceProvider _services;
        private static ulong _channelId = LoadChannelId();
        private static Form1 _desktopWindow;
        private static EventManager _eventManager;
        private static string BotToken = LoadBotToken();

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Form1 desktopWindow = new Form1();

            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "log.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, logconsole);
            config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            desktopWindow.Load += async (sender, e) => await DiscordClient(desktopWindow);
            desktopWindow.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.F10)
                {
                    OpenChannelInputForm();
                }
            };

            Application.Run(desktopWindow);
        }

        static async Task DiscordClient(Form1 desktopWindow)
        {
            _desktopWindow = desktopWindow;

            _services = new ServiceCollection()
                .AddOptions()
                .Configure<DiscordServiceOptions>(options =>
                {
                    options.BotToken = BotToken;
                })
                .AddSingleton<EventManager>()
                .AddSingleton<IDiscordService, EventManager>()
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

            _eventManager = _services.GetService<EventManager>();
            if (_eventManager != null)
            {
                _eventManager.SetDesktopWindow(_desktopWindow);
                await _eventManager.InitializeClient();
                await _eventManager.ChangeChannel(_channelId);
            }
        }

        static async Task RestartDiscordClient()
        {
            if (_eventManager != null)
            {
                await _eventManager.ChangeChannel(_channelId);
            }
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

            confirmButton.Click += async (sender, e) =>
            {
                if (ulong.TryParse(inputBox.Text, out ulong newChannelId))
                {
                    _channelId = newChannelId;
                    SaveChannelId(newChannelId);
                    await RestartDiscordClient();
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