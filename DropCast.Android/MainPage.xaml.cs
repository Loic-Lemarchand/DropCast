using Android.Content;
using DropCast.Android.Platforms;
using DropCast.Android.Services;
using Microsoft.Extensions.Logging;

namespace DropCast.Android;

public partial class MainPage : ContentPage
{
	private readonly DiscordService _discord;
	private readonly VideoResolver _resolver;
	private readonly ILogger<OverlayService> _overlayLogger;

	private List<DiscordService.DiscordGuildInfo> _guilds = [];
	private List<DiscordService.DiscordChannelInfo> _channels = [];
	private List<ChannelHistoryEntry> _recentEntries = [];

	public MainPage(DiscordService discord, VideoResolver resolver, ILogger<OverlayService> overlayLogger)
	{
		InitializeComponent();
		_discord = discord;
		_resolver = resolver;
		_overlayLogger = overlayLogger;

		OpacitySlider.Value = AppSettings.OverlayBackgroundOpacity;
		OpacityValueLabel.Text = $"{AppSettings.OverlayBackgroundOpacity} %";
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (!_discord.IsConnected)
			await TryAutoConnect();
	}

	private async Task TryAutoConnect()
	{
		// Try extracting the bundled token on first launch
		await TokenProvider.EnsureTokenFromBundleAsync();

		string? token = TokenProvider.LoadEncrypted(TokenProvider.GetTokenFilePath());
		if (string.IsNullOrEmpty(token))
		{
			// No bundled or local token — fall back to manual entry
			TokenSection.IsVisible = true;
			BotStatusLabel.Text = "Entre le token du bot pour commencer";
			BotStatusLabel.TextColor = Colors.Orange;
			return;
		}

		BotStatusLabel.Text = "⏳ Connexion automatique...";
		BotStatusLabel.TextColor = Colors.Yellow;

		try
		{
			await _discord.ConnectAsync(token);

			// Migrate known servers from channel history for users updating from older versions
			AppSettings.MigrateKnownServers();

			PopulateFilteredGuilds();

			ChannelPickerSection.IsVisible = true;
			BotStatusLabel.Text = $"✅ Connecté — {_guilds.Count} serveur(s)";
			BotStatusLabel.TextColor = Colors.LimeGreen;

			PopulateRecentChannels();
			TryRestoreSavedChannel();
		}
		catch
		{
			BotStatusLabel.Text = "❌ Auto-connexion échouée";
			BotStatusLabel.TextColor = Colors.Red;
			TokenSection.IsVisible = true;
		}
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
			TokenProvider.SaveEncrypted(token, TokenProvider.GetTokenFilePath());

			// Migrate known servers from channel history for users updating from older versions
			AppSettings.MigrateKnownServers();

			PopulateFilteredGuilds();

			ChannelPickerSection.IsVisible = true;
			TokenSection.IsVisible = false;
			BotStatusLabel.Text = $"✅ Connecté — {_guilds.Count} serveur(s)";
			BotStatusLabel.TextColor = Colors.LimeGreen;

			PopulateRecentChannels();
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

		int serverIdx = ServerPicker.SelectedIndex;
		if (serverIdx >= 0 && serverIdx < _guilds.Count)
		{
			var guild = _guilds[serverIdx];
			AppSettings.AddToChannelHistory(guild.Id, guild.Name, channel.Id, channel.Name);
		}
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

	private void PopulateRecentChannels()
	{
		_recentEntries = AppSettings.GetChannelHistory();
		if (_recentEntries.Count == 0)
		{
			RecentLabel.IsVisible = false;
			RecentBorder.IsVisible = false;
			return;
		}

		RecentLabel.IsVisible = true;
		RecentBorder.IsVisible = true;
		RecentPicker.Items.Clear();
		foreach (var entry in _recentEntries)
			RecentPicker.Items.Add($"{entry.ServerName} → #{entry.ChannelName}");
	}

	private void OnRecentChannelChanged(object? sender, EventArgs e)
	{
		int idx = RecentPicker.SelectedIndex;
		if (idx < 0 || idx >= _recentEntries.Count) return;

		var entry = _recentEntries[idx];
		_discord.ChannelId = entry.ChannelId;
		AppSettings.ServerId = entry.ServerId;
		AppSettings.ChannelId = entry.ChannelId;

		for (int i = 0; i < _guilds.Count; i++)
		{
			if (_guilds[i].Id != entry.ServerId) continue;
			ServerPicker.SelectedIndex = i;
			for (int j = 0; j < _channels.Count; j++)
			{
				if (_channels[j].Id != entry.ChannelId) continue;
				ChannelPicker.SelectedIndex = j;
				break;
			}
			break;
		}
	}

	private async void OnAddServerClicked(object? sender, EventArgs e)
	{
		string input = InviteEntry.Text?.Trim() ?? "";
		string code = DiscordService.ParseInviteCode(input);
		if (string.IsNullOrEmpty(code))
		{
			InviteStatusLabel.Text = "Collez un lien d'invitation valide.";
			InviteStatusLabel.TextColor = Colors.Red;
			return;
		}

		AddServerButton.IsEnabled = false;
		InviteStatusLabel.Text = "⏳ Résolution de l'invitation...";
		InviteStatusLabel.TextColor = Colors.Yellow;

		try
		{
			var info = await _discord.ResolveInviteAsync(code);
			if (info == null)
			{
				InviteStatusLabel.Text = "❌ Invitation invalide ou expirée.";
				InviteStatusLabel.TextColor = Colors.Red;
				return;
			}

			if (_discord.IsInGuild(info.Value.GuildId))
				{
					InviteStatusLabel.Text = $"✅ Déjà dans « {info.Value.GuildName} ».";
					InviteStatusLabel.TextColor = Colors.LimeGreen;
					AppSettings.AddKnownServer(info.Value.GuildId);
					RefreshServerList(info.Value.GuildId);
					return;
				}

				string url = _discord.GetBotInviteUrl(info.Value.GuildId);
				await Launcher.OpenAsync(url);

				bool confirmed = await DisplayAlert(
					"Ajout du bot",
					"Autorisez le bot dans votre navigateur,\npuis appuyez OK pour rafraîchir.",
					"OK", "Annuler");

				if (confirmed)
				{
					AppSettings.AddKnownServer(info.Value.GuildId);
					RefreshServerList(info.Value.GuildId);
				InviteStatusLabel.Text = _discord.IsInGuild(info.Value.GuildId)
					? "✅ Bot ajouté !"
					: "⚠️ Le bot n'a pas encore rejoint.";
				InviteStatusLabel.TextColor = _discord.IsInGuild(info.Value.GuildId)
					? Colors.LimeGreen
					: Colors.Orange;
			}
		}
		catch (Exception ex)
		{
			InviteStatusLabel.Text = $"❌ {ex.Message}";
			InviteStatusLabel.TextColor = Colors.Red;
		}
		finally
		{
			AddServerButton.IsEnabled = true;
		}
	}

	private void RefreshServerList(ulong selectGuildId = 0)
	{
		PopulateFilteredGuilds();

		if (selectGuildId != 0)
		{
			for (int i = 0; i < _guilds.Count; i++)
			{
				if (_guilds[i].Id == selectGuildId)
				{
					ServerPicker.SelectedIndex = i;
					break;
				}
			}
		}
	}

	private void PopulateFilteredGuilds()
	{
		var allGuilds = _discord.GetGuilds();
		var knownIds = new HashSet<ulong>(AppSettings.GetKnownServerIds());
		_guilds = allGuilds.Where(g => knownIds.Contains(g.Id)).ToList();
		ServerPicker.Items.Clear();
		foreach (var g in _guilds)
			ServerPicker.Items.Add(g.Name);
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
		TokenSection.IsVisible = true;
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

	private void OnOpacityChanged(object? sender, ValueChangedEventArgs e)
	{
		int value = (int)Math.Round(e.NewValue);
		AppSettings.OverlayBackgroundOpacity = value;
		OpacityValueLabel.Text = $"{value} %";
	}

	private static void OpenOverlaySettings()
	{
		var intent = new Intent(global::Android.Provider.Settings.ActionManageOverlayPermission,
			global::Android.Net.Uri.Parse("package:" + Platform.CurrentActivity!.PackageName));
		Platform.CurrentActivity.StartActivity(intent);
	}
}
