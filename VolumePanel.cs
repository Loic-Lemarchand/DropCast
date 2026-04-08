using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropCast
{
    public class VolumePanel : Form
    {
        private TrackBar _trackBar;
        private Label _volumeLabel;

        public event EventHandler<int> VolumeChanged;

        public int Volume
        {
            get { return _trackBar.Value; }
            set
            {
                _trackBar.Value = Math.Max(_trackBar.Minimum, Math.Min(_trackBar.Maximum, value));
                UpdateLabel();
            }
        }

        public VolumePanel()
        {
            SuspendLayout();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            Size = new Size(280, 80);
            Padding = new Padding(16, 12, 16, 12);
            DoubleBuffered = true;

            var titleLabel = new Label
            {
                Text = "🔊 Volume DropCast",
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
                UpdateLabel();
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

            Controls.Add(titleLabel);
            Controls.Add(_trackBar);
            Controls.Add(_volumeLabel);

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

        private void UpdateLabel()
        {
            _volumeLabel.Text = _trackBar.Value + "%";
        }
    }
}
