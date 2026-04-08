using System;
using System.Drawing;
using System.Windows.Forms;

namespace DropCast
{
    public class ControlPanel : Form
    {
        private TrackBar _trackBar;
        private Label _volumeLabel;
        private CheckBox _clickToDismissCheck;
        private CheckBox _showAuthorCheck;

        public event EventHandler<int> VolumeChanged;
        public event EventHandler<bool> ClickToDismissChanged;
        public event EventHandler<bool> ShowAuthorInfoChanged;

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

        public ControlPanel()
        {
            SuspendLayout();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            Size = new Size(280, 140);
            Padding = new Padding(16, 12, 16, 12);
            DoubleBuffered = true;

            var titleLabel = new Label
            {
                Text = "🎛️ DropCast",
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 10),
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
                Location = new Point(14, 34),
                Size = new Size(200, 30),
                BackColor = Color.FromArgb(30, 30, 30)
            };
            _trackBar.ValueChanged += (s, e) =>
            {
                UpdateVolumeLabel();
                VolumeChanged?.Invoke(this, _trackBar.Value);
            };

            _volumeLabel = new Label
            {
                Text = "100%",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Size = new Size(50, 24),
                Location = new Point(216, 38),
                BackColor = Color.Transparent
            };

            _clickToDismissCheck = new CheckBox
            {
                Text = "🖱️ Clic pour couper",
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(16, 74),
                BackColor = Color.Transparent,
                Checked = false
            };
            _clickToDismissCheck.CheckedChanged += (s, e) =>
                ClickToDismissChanged?.Invoke(this, _clickToDismissCheck.Checked);

            _showAuthorCheck = new CheckBox
            {
                Text = "👤 Afficher l'auteur",
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(16, 100),
                BackColor = Color.Transparent,
                Checked = false
            };
            _showAuthorCheck.CheckedChanged += (s, e) =>
                ShowAuthorInfoChanged?.Invoke(this, _showAuthorCheck.Checked);

            Controls.Add(titleLabel);
            Controls.Add(_trackBar);
            Controls.Add(_volumeLabel);
            Controls.Add(_clickToDismissCheck);
            Controls.Add(_showAuthorCheck);

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
            using (var pen = new Pen(Color.FromArgb(80, 80, 80), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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
