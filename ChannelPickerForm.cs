using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DropCast.Sources;

namespace DropCast
{
    public class ChannelPickerForm : Form
    {
        private readonly DiscordMessageSource _source;
        private ListBox _historyList;
        private ComboBox _serverCombo;
        private ComboBox _channelCombo;
        private TextBox _inviteBox;
        private Button _addServerButton;
        private Button _confirmButton;
        private Label _statusLabel;
        private ulong _preselectedChannelId;

        public ulong SelectedServerId { get; private set; }
        public string SelectedServerName { get; private set; }
        public ulong SelectedChannelId { get; private set; }
        public string SelectedChannelName { get; private set; }

        public ChannelPickerForm(DiscordMessageSource source, ulong currentServerId, ulong currentChannelId, List<ChannelHistoryEntry> history)
        {
            _source = source;
            _preselectedChannelId = currentChannelId;
            BuildLayout(history ?? new List<ChannelHistoryEntry>());
            PopulateServers(currentServerId);
        }

        private void BuildLayout(List<ChannelHistoryEntry> history)
        {
            Text = "📡 Sélection du canal Discord";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            int y = 14;
            int left = 20;
            int w = 340;

            // --- Recent channels ---
            if (history.Count > 0)
            {
                Controls.Add(new Label
                {
                    Text = "Canaux récents (double-clic pour sélectionner) :",
                    Left = left, Top = y, AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold)
                });
                y += 22;

                _historyList = new ListBox
                {
                    Left = left, Top = y,
                    Width = w, Height = Math.Min(history.Count * 18 + 4, 90),
                    IntegralHeight = false
                };
                foreach (var entry in history)
                    _historyList.Items.Add(new HistoryItem(entry));
                _historyList.DoubleClick += OnHistoryDoubleClick;
                Controls.Add(_historyList);
                y += _historyList.Height + 10;
            }

            // --- Manual selection ---
            Controls.Add(new Label
            {
                Text = "── Sélection manuelle ──",
                Left = left, Top = y, Width = w,
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter
            });
            y += 24;

            Controls.Add(new Label { Text = "Serveur :", Left = left, Top = y, AutoSize = true });
            y += 18;

            _serverCombo = new ComboBox
            {
                Left = left, Top = y, Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _serverCombo.SelectedIndexChanged += OnServerChanged;
            Controls.Add(_serverCombo);
            y += 30;

            Controls.Add(new Label { Text = "Canal :", Left = left, Top = y, AutoSize = true });
            y += 18;

            _channelCombo = new ComboBox
            {
                Left = left, Top = y, Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            Controls.Add(_channelCombo);
            y += 34;

            // --- Add server ---
            Controls.Add(new Label
            {
                Text = "── Ajouter un serveur ──",
                Left = left, Top = y, Width = w,
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter
            });
            y += 24;

            Controls.Add(new Label { Text = "Lien d'invitation Discord :", Left = left, Top = y, AutoSize = true });
            y += 18;

            _inviteBox = new TextBox { Left = left, Top = y, Width = w };
            Controls.Add(_inviteBox);
            y += 28;

            _addServerButton = new Button
            {
                Text = "Ajouter le bot au serveur",
                Left = left, Top = y, Width = w, Height = 28
            };
            _addServerButton.Click += OnAddServerClicked;
            Controls.Add(_addServerButton);
            y += 34;

            _statusLabel = new Label
            {
                Text = "", Left = left, Top = y, Width = w,
                ForeColor = Color.Gray, Height = 18
            };
            Controls.Add(_statusLabel);
            y += 24;

            // --- Validate ---
            _confirmButton = new Button
            {
                Text = "Valider",
                Left = (380 - 120) / 2, Top = y,
                Width = 120, Height = 32
            };
            _confirmButton.Click += OnConfirmClicked;
            Controls.Add(_confirmButton);

            ClientSize = new Size(380, y + 46);
        }

        private void PopulateServers(ulong preselectedServerId)
        {
            var guilds = _source.GetGuilds();
            _serverCombo.Items.Clear();

            foreach (var g in guilds)
                _serverCombo.Items.Add(new ComboItem(g.Id, g.Name));

            if (preselectedServerId != 0)
            {
                for (int i = 0; i < _serverCombo.Items.Count; i++)
                {
                    if (((ComboItem)_serverCombo.Items[i]).Id == preselectedServerId)
                    {
                        _serverCombo.SelectedIndex = i;
                        return;
                    }
                }
            }

            if (_serverCombo.Items.Count > 0)
                _serverCombo.SelectedIndex = 0;
        }

        private void OnServerChanged(object sender, EventArgs e)
        {
            if (!(_serverCombo.SelectedItem is ComboItem guild)) return;

            var channels = _source.GetTextChannels(guild.Id);
            _channelCombo.Items.Clear();

            foreach (var c in channels)
                _channelCombo.Items.Add(new ComboItem(c.Id, c.Name));

            bool preselected = false;
            if (_preselectedChannelId != 0)
            {
                for (int i = 0; i < _channelCombo.Items.Count; i++)
                {
                    if (((ComboItem)_channelCombo.Items[i]).Id == _preselectedChannelId)
                    {
                        _channelCombo.SelectedIndex = i;
                        preselected = true;
                        break;
                    }
                }
                _preselectedChannelId = 0;
            }

            if (!preselected && _channelCombo.Items.Count > 0)
                _channelCombo.SelectedIndex = 0;
        }

        private void OnHistoryDoubleClick(object sender, EventArgs e)
        {
            if (!(_historyList.SelectedItem is HistoryItem item)) return;
            SelectedServerId = item.Entry.ServerId;
            SelectedServerName = item.Entry.ServerName;
            SelectedChannelId = item.Entry.ChannelId;
            SelectedChannelName = item.Entry.ChannelName;
            DialogResult = DialogResult.OK;
            Close();
        }

        private async void OnAddServerClicked(object sender, EventArgs e)
        {
            string code = DiscordMessageSource.ParseInviteCode(_inviteBox.Text);
            if (string.IsNullOrEmpty(code))
            {
                _statusLabel.Text = "Collez un lien d'invitation valide.";
                _statusLabel.ForeColor = Color.Red;
                return;
            }

            _addServerButton.Enabled = false;
            _statusLabel.Text = "⏳ Résolution de l'invitation...";
            _statusLabel.ForeColor = SystemColors.ControlText;

            try
            {
                var info = await _source.ResolveInviteAsync(code);
                if (info == null)
                {
                    _statusLabel.Text = "❌ Invitation invalide ou expirée.";
                    _statusLabel.ForeColor = Color.Red;
                    return;
                }

                if (_source.IsInGuild(info.GuildId))
                {
                    _statusLabel.Text = "✅ Déjà dans « " + info.GuildName + " ».";
                    _statusLabel.ForeColor = Color.Green;
                    PopulateServers(info.GuildId);
                    return;
                }

                string url = _source.GetBotInviteUrl(info.GuildId);
                Process.Start(url);

                MessageBox.Show(
                    "Autorisez le bot dans votre navigateur,\npuis cliquez OK pour rafraîchir la liste.",
                    "Ajout du bot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                PopulateServers(info.GuildId);

                if (_source.IsInGuild(info.GuildId))
                {
                    _statusLabel.Text = "✅ Bot ajouté !";
                    _statusLabel.ForeColor = Color.Green;
                }
                else
                {
                    _statusLabel.Text = "⚠️ Bot pas encore rejoint. Réessayez.";
                    _statusLabel.ForeColor = Color.Orange;
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "❌ " + ex.Message;
                _statusLabel.ForeColor = Color.Red;
            }
            finally
            {
                _addServerButton.Enabled = true;
            }
        }

        private void OnConfirmClicked(object sender, EventArgs e)
        {
            if (_serverCombo.SelectedItem is ComboItem guild && _channelCombo.SelectedItem is ComboItem channel)
            {
                SelectedServerId = guild.Id;
                SelectedServerName = guild.Name;
                SelectedChannelId = channel.Id;
                SelectedChannelName = channel.Name;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Veuillez sélectionner un serveur et un canal.", "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private class ComboItem
        {
            public ulong Id { get; }
            public string Name { get; }
            public ComboItem(ulong id, string name) { Id = id; Name = name; }
            public override string ToString() => Name;
        }

        private class HistoryItem
        {
            public ChannelHistoryEntry Entry { get; }
            public HistoryItem(ChannelHistoryEntry entry) { Entry = entry; }
            public override string ToString() => Entry.ServerName + " → #" + Entry.ChannelName;
        }
    }
}
