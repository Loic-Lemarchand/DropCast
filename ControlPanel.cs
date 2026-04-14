using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropCast
{
    public class ControlPanel : Form
    {
        // ── Palette ──
        private static readonly Color BgDark = Color.FromArgb(13, 43, 62);
        private static readonly Color BgCard = Color.FromArgb(20, 61, 84);
        private static readonly Color Accent = Color.FromArgb(42, 191, 191);
        private static readonly Color TextPrimary = Color.White;
        private static readonly Color TextSecondary = Color.FromArgb(139, 184, 196);
        private static readonly Color BorderColor = Color.FromArgb(30, 74, 95);

        private TrackBar _trackBar;
        private Label _volumeLabel;
        private CheckBox _clickToDismissCheck;
        private CheckBox _showAuthorCheck;
        private Button _changeChannelButton;
        private Label _channelInfoLabel;

        public event EventHandler<int> VolumeChanged;
        public event EventHandler<bool> ClickToDismissChanged;
        public event EventHandler<bool> ShowAuthorInfoChanged;
        public event EventHandler ChangeChannelRequested;

        public int Volume
        {
            get { return _trackBar.Value; }
            set
            {
                _trackBar.Value = Math.Max(_trackBar.Minimum, Math.Min(_trackBar.Maximum, value));
                UpdateVolumeLabel();
            }
        }

        public bool ClickToDismissEnabled
        {
            get { return _clickToDismissCheck.Checked; }
            set { _clickToDismissCheck.Checked = value; }
        }

        public bool ShowAuthorInfoEnabled
        {
            get { return _showAuthorCheck.Checked; }
            set { _showAuthorCheck.Checked = value; }
        }

        public void SetChannelInfo(string serverName, string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                _channelInfoLabel.Text = "Aucun canal sélectionné";
            else
                _channelInfoLabel.Text = serverName + "  →  #" + channelName;
        }

        public ControlPanel()
        {
            SuspendLayout();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = BgDark;
            Opacity = 0.92;
            Size = new Size(300, 230);
            Padding = new Padding(16, 12, 16, 12);
            DoubleBuffered = true;

            var titleLabel = new Label
            {
                Text = "🎛️ DropCast",
                ForeColor = Accent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };

            var shortcutHint = new Label
            {
                Text = "F10",
                ForeColor = Color.FromArgb(60, 110, 130),
                Font = new Font("Segoe UI", 8F),
                AutoSize = true,
                Location = new Point(252, 16),
                BackColor = Color.Transparent
            };

            _trackBar = new TrackBar
            {
                Minimum = 0,
                Maximum = 200,
                Value = 100,
                TickFrequency = 25,
                SmallChange = 5,
                LargeChange = 10,
                Location = new Point(14, 40),
                Size = new Size(220, 30),
                BackColor = BgDark
            };
            _trackBar.ValueChanged += (s, e) =>
            {
                UpdateVolumeLabel();
                VolumeChanged?.Invoke(this, _trackBar.Value);
            };

            _volumeLabel = new Label
            {
                Text = "100%",
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Size = new Size(50, 24),
                Location = new Point(236, 44),
                BackColor = Color.Transparent
            };

            _clickToDismissCheck = new CheckBox
            {
                Text = "🖱️ Clic pour couper",
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(16, 80),
                BackColor = Color.Transparent,
                Checked = false
            };
            _clickToDismissCheck.CheckedChanged += (s, e) =>
                ClickToDismissChanged?.Invoke(this, _clickToDismissCheck.Checked);

            _showAuthorCheck = new CheckBox
            {
                Text = "👤 Afficher l'auteur",
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(16, 108),
                BackColor = Color.Transparent,
                Checked = false
            };
            _showAuthorCheck.CheckedChanged += (s, e) =>
                ShowAuthorInfoChanged?.Invoke(this, _showAuthorCheck.Checked);

            // ── Channel section ──
            var separator = new Label
            {
                Text = "",
                Location = new Point(16, 138),
                Size = new Size(268, 1),
                BackColor = BorderColor
            };

            _channelInfoLabel = new Label
            {
                Text = "Aucun canal sélectionné",
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 148),
                Size = new Size(268, 18),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _changeChannelButton = new Button
            {
                Text = "📡  Changer de canal",
                FlatStyle = FlatStyle.Flat,
                BackColor = BgCard,
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 172),
                Size = new Size(268, 30),
                Cursor = Cursors.Hand
            };
            _changeChannelButton.FlatAppearance.BorderColor = BorderColor;
            _changeChannelButton.FlatAppearance.BorderSize = 1;
            _changeChannelButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 80, 105);
            _changeChannelButton.Click += (s, e) =>
            {
                Hide();
                ChangeChannelRequested?.Invoke(this, EventArgs.Empty);
            };

            Controls.Add(titleLabel);
            Controls.Add(shortcutHint);
            Controls.Add(_trackBar);
            Controls.Add(_volumeLabel);
            Controls.Add(_clickToDismissCheck);
            Controls.Add(_showAuthorCheck);
            Controls.Add(separator);
            Controls.Add(_channelInfoLabel);
            Controls.Add(_changeChannelButton);

            ResumeLayout(false);
        }

        public void PositionOnScreen()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Right - Width - 20, screen.Bottom - Height - 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(BorderColor, 1))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }

        private void UpdateVolumeLabel()
        {
            _volumeLabel.Text = _trackBar.Value + "%";
        }
    }
}
