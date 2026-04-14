using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DropCast.Sources;

namespace DropCast
{
    public class ChannelPickerForm : Form
    {
        // ── Palette ──
        private static readonly Color BgDark = Color.FromArgb(13, 43, 62);
        private static readonly Color BgCard = Color.FromArgb(20, 61, 84);
        private static readonly Color Accent = Color.FromArgb(42, 191, 191);
        private static readonly Color AccentLight = Color.FromArgb(72, 209, 204);
        private static readonly Color TextPrimary = Color.White;
        private static readonly Color TextSecondary = Color.FromArgb(139, 184, 196);
        private static readonly Color InputBg = Color.FromArgb(26, 58, 80);
        private static readonly Color Border = Color.FromArgb(30, 74, 95);

        private readonly DiscordMessageSource _source;
        private ListBox _historyList;
        private ComboBox _serverCombo;
        private ComboBox _channelCombo;
        private TextBox _inviteBox;
        private Button _addServerButton;
        private Button _confirmButton;
        private Label _statusLabel;
        private ulong _preselectedChannelId;
        private readonly List<ulong> _knownServerIds;

        public ulong SelectedServerId { get; private set; }
        public string SelectedServerName { get; private set; }
        public ulong SelectedChannelId { get; private set; }
        public string SelectedChannelName { get; private set; }

        public ChannelPickerForm(DiscordMessageSource source, ulong currentServerId, ulong currentChannelId, List<ChannelHistoryEntry> history, List<ulong> knownServerIds)
        {
            _source = source;
            _knownServerIds = knownServerIds;
            _preselectedChannelId = currentChannelId;
            BuildLayout(history ?? new List<ChannelHistoryEntry>());
            PopulateServers(currentServerId);
        }

        private static Button MakeButton(string text, Color bg, int left, int top, int w, int h)
        {
            var btn = new Button
            {
                Text = text,
                Left = left, Top = top, Width = w, Height = h,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15F);
            return btn;
        }

        private static Label MakeSection(string text, int left, int top, int w)
        {
            return new Label
            {
                Text = text,
                Left = left, Top = top, Width = w,
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 18
            };
        }

        private void BuildLayout(List<ChannelHistoryEntry> history)
        {
            Text = "DropCast — Sélection du canal";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = BgDark;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5F);

            int y = 20;
            int left = 24;
            int w = 340;

            // ── Title ──
            Controls.Add(new Label
            {
                Text = "📡  Sélection du canal",
                Left = left, Top = y, AutoSize = true,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            y += 36;

            // --- Recent channels ---
            if (history.Count > 0)
            {
                Controls.Add(new Label
                {
                    Text = "CANAUX RÉCENTS",
                    Left = left, Top = y, AutoSize = true,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    ForeColor = Accent
                });
                y += 20;

                _historyList = new ListBox
                {
                    Left = left, Top = y,
                    Width = w, Height = Math.Min(history.Count * 22 + 4, 92),
                    IntegralHeight = false,
                    BackColor = InputBg,
                    ForeColor = TextPrimary,
                    Font = new Font("Segoe UI", 9.5F),
                    BorderStyle = BorderStyle.None
                };
                foreach (var entry in history)
                    _historyList.Items.Add(new HistoryItem(entry));
                _historyList.DoubleClick += OnHistoryDoubleClick;
                Controls.Add(_historyList);
                y += _historyList.Height + 14;
            }

            // --- Manual selection ---
            Controls.Add(MakeSection("── Sélection manuelle ──", left, y, w));
            y += 26;

            Controls.Add(new Label
            {
                Text = "SERVEUR",
                Left = left, Top = y, AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextSecondary
            });
            y += 18;

            _serverCombo = new ComboBox
            {
                Left = left, Top = y, Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = InputBg,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F)
            };
            _serverCombo.SelectedIndexChanged += OnServerChanged;
            Controls.Add(_serverCombo);
            y += 32;

            Controls.Add(new Label
            {
                Text = "CANAL",
                Left = left, Top = y, AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextSecondary
            });
            y += 18;

            _channelCombo = new ComboBox
            {
                Left = left, Top = y, Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = InputBg,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F)
            };
            Controls.Add(_channelCombo);
            y += 36;

            // --- Add server ---
            Controls.Add(MakeSection("── Ajouter un serveur ──", left, y, w));
            y += 26;

            Controls.Add(new Label
            {
                Text = "LIEN D'INVITATION",
                Left = left, Top = y, AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextSecondary
            });
            y += 18;

            _inviteBox = new TextBox
            {
                Left = left, Top = y, Width = w,
                BackColor = InputBg,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_inviteBox);
            y += 30;

            _addServerButton = MakeButton("Ajouter le bot au serveur", BgCard, left, y, w, 32);
            _addServerButton.Click += OnAddServerClicked;
            Controls.Add(_addServerButton);
            y += 38;

            _statusLabel = new Label
            {
                Text = "", Left = left, Top = y, Width = w,
                ForeColor = TextSecondary, Height = 18,
                Font = new Font("Segoe UI", 8.5F)
            };
            Controls.Add(_statusLabel);
            y += 24;

            // --- Validate ---
            _confirmButton = MakeButton("Valider", Accent, (388 - 160) / 2, y, 160, 36);
            _confirmButton.Click += OnConfirmClicked;
            Controls.Add(_confirmButton);

            ClientSize = new Size(388, y + 52);
        }

        private void PopulateServers(ulong preselectedServerId)
        {
            var allGuilds = _source.GetGuilds();
            var knownSet = new HashSet<ulong>(_knownServerIds);
            var guilds = allGuilds.FindAll(g => knownSet.Contains(g.Id));
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
                    if (!_knownServerIds.Contains(info.GuildId))
                        _knownServerIds.Add(info.GuildId);
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

                if (!_knownServerIds.Contains(info.GuildId))
                    _knownServerIds.Add(info.GuildId);
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
