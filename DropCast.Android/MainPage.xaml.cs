using Android.Content;
using DropCast.Android.Platforms;
using DropCast.Android.Services;
using Microsoft.Extensions.Logging;

namespace DropCast.Android;

public partial class MainPage : ContentPage
{
	private readonly DiscordService _discord;
	private readonly WhatsAppService _whatsApp;
	private readonly VideoResolver _resolver;
	private readonly ILogger<OverlayService> _overlayLogger;

	private List<DiscordService.DiscordGuildInfo> _guilds = [];
	private List<DiscordService.DiscordChannelInfo> _channels = [];

	public MainPage(DiscordService discord, WhatsAppService whatsApp, VideoResolver resolver, ILogger<OverlayService> overlayLogger)
	{
		InitializeComponent();
		_discord = discord;
		_whatsApp = whatsApp;
		_resolver = resolver;
		_overlayLogger = overlayLogger;

		TokenEntry.Text = AppSettings.BotToken;
	}

	private async void OnConnectBotClicked(object? sender, EventArgs e)
	{
		string token = TokenEntry.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(token))
		{
			BotStatusLabel.Text = "❌ Entre un token";
			BotStatusLabel.TextColor = Colors.Red;
			return;
		}

		ConnectBotButton.IsEnabled = false;
		BotStatusLabel.Text = "⏳ Connexion...";
		BotStatusLabel.TextColor = Colors.Yellow;

		try
		{
			await _discord.ConnectAsync(token);
			AppSettings.BotToken = token;

			_guilds = _discord.GetGuilds();
			ServerPicker.Items.Clear();
			foreach (var g in _guilds)
				ServerPicker.Items.Add(g.Name);

			ChannelPickerSection.IsVisible = true;
			BotStatusLabel.Text = $"✅ Connecté — {_guilds.Count} serveur(s)";
			BotStatusLabel.TextColor = Colors.LimeGreen;

			TryRestoreSavedChannel();
		}
		catch (Exception ex)
		{
			BotStatusLabel.Text = $"❌ {ex.Message}";
			BotStatusLabel.TextColor = Colors.Red;
		}
		finally
		{
			ConnectBotButton.IsEnabled = true;
		}
	}

	private void OnServerChanged(object? sender, EventArgs e)
	{
		int idx = ServerPicker.SelectedIndex;
		if (idx < 0 || idx >= _guilds.Count) return;

		var guild = _guilds[idx];
		AppSettings.ServerId = guild.Id;

		_channels = _discord.GetTextChannels(guild.Id);
		ChannelPicker.Items.Clear();
		foreach (var c in _channels)
			ChannelPicker.Items.Add("#" + c.Name);

		ChannelPicker.SelectedIndex = -1;
	}

	private void OnChannelChanged(object? sender, EventArgs e)
	{
		int idx = ChannelPicker.SelectedIndex;
		if (idx < 0 || idx >= _channels.Count) return;

		var channel = _channels[idx];
		_discord.ChannelId = channel.Id;
		AppSettings.ChannelId = channel.Id;
	}

	private void TryRestoreSavedChannel()
	{
		ulong savedServer = AppSettings.ServerId;
		ulong savedChannel = AppSettings.ChannelId;
		if (savedServer == 0 || savedChannel == 0) return;

		for (int i = 0; i < _guilds.Count; i++)
		{
			if (_guilds[i].Id != savedServer) continue;

			ServerPicker.SelectedIndex = i;
			// OnServerChanged fires and populates _channels
			for (int j = 0; j < _channels.Count; j++)
			{
				if (_channels[j].Id != savedChannel) continue;
				ChannelPicker.SelectedIndex = j;
				break;
			}
			break;
		}
	}

	private async void OnStartClicked(object? sender, EventArgs e)
	{
		if (!_discord.IsConnected)
		{
			StatusLabel.Text = "❌ Connecte le bot d'abord";
			StatusLabel.TextColor = Colors.Red;
			return;
		}

		if (_discord.ChannelId == 0)
		{
			StatusLabel.Text = "❌ Choisis un salon Discord";
			StatusLabel.TextColor = Colors.Red;
			return;
		}

		if (!global::Android.Provider.Settings.CanDrawOverlays(Platform.CurrentActivity))
		{
			StatusLabel.Text = "⚠️ Permission overlay requise";
			StatusLabel.TextColor = Colors.Orange;
			OpenOverlaySettings();
			return;
		}

		StartButton.IsEnabled = false;
		StatusLabel.Text = "⏳ Démarrage...";
		StatusLabel.TextColor = Colors.Yellow;

		try
		{
			OverlayService.Discord = _discord;
			OverlayService.WhatsApp = _whatsApp;
			OverlayService.Resolver = _resolver;
			OverlayService.Logger = _overlayLogger;

			var intent = new Intent(Platform.CurrentActivity, typeof(OverlayService));
			Platform.CurrentActivity!.StartForegroundService(intent);

			StatusLabel.Text = "🟢 Discord — en attente de memes";
			StatusLabel.TextColor = Colors.LimeGreen;
			StartButton.IsVisible = false;
			StopButton.IsVisible = true;
		}
		catch (Exception ex)
		{
			StatusLabel.Text = $"❌ {ex.Message}";
			StatusLabel.TextColor = Colors.Red;
			StartButton.IsEnabled = true;
		}
	}

	private async void OnStopClicked(object? sender, EventArgs e)
	{
		try
		{
			var intent = new Intent(Platform.CurrentActivity, typeof(OverlayService));
			intent.SetAction("STOP");
			Platform.CurrentActivity!.StartService(intent);
			await _discord.DisconnectAsync();
		}
		catch { }

		StatusLabel.Text = "⚪ Déconnecté";
		StatusLabel.TextColor = Colors.Gray;
		StartButton.IsVisible = true;
		StartButton.IsEnabled = true;
		StopButton.IsVisible = false;

		ChannelPickerSection.IsVisible = false;
		BotStatusLabel.Text = "";
	}

	private void OnOverlayPermissionClicked(object? sender, EventArgs e)
	{
		OpenOverlaySettings();
	}

	private async void OnZoneConfigClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("OverlayZone");
	}

	private static void OpenOverlaySettings()
	{
		var intent = new Intent(global::Android.Provider.Settings.ActionManageOverlayPermission,
			global::Android.Net.Uri.Parse("package:" + Platform.CurrentActivity!.PackageName));
		Platform.CurrentActivity.StartActivity(intent);
	}
}
