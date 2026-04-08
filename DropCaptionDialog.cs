using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DropCast
{
    /// <summary>
    /// Modal dialog shown after a file drop to let the user enter a caption and optional trim values.
    /// </summary>
    public class DropCaptionDialog : Form
    {
        private TextBox _captionBox;
        private TextBox _trimStartBox;
        private TextBox _trimEndBox;

        public string Caption { get; private set; }
        public double? TrimStart { get; private set; }
        public double? TrimEnd { get; private set; }

        public DropCaptionDialog(string filePath)
        {
            Text = "DropCast — Envoyer un meme";
            Size = new Size(420, 270);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            var fileLabel = new Label
            {
                Text = "📂  " + Path.GetFileName(filePath),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                AutoSize = true,
                Location = new Point(20, 15)
            };

            var captionLabel = new Label
            {
                Text = "Caption :",
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(20, 50)
            };

            _captionBox = new TextBox
            {
                Location = new Point(20, 72),
                Size = new Size(360, 24),
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White
            };

            var trimLabel = new Label
            {
                Text = "Trim (optionnel) :",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoSize = true,
                Location = new Point(20, 110)
            };

            var trimStartLabel = new Label
            {
                Text = "Début (s) :",
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(20, 136)
            };

            _trimStartBox = new TextBox
            {
                Location = new Point(100, 133),
                Size = new Size(80, 24),
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White
            };

            var trimEndLabel = new Label
            {
                Text = "Fin (s) :",
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(210, 136)
            };

            _trimEndBox = new TextBox
            {
                Location = new Point(270, 133),
                Size = new Size(80, 24),
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White
            };

            var validateButton = new Button
            {
                Text = "🚀 Envoyer",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(130, 180),
                Size = new Size(150, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 120, 200),
                ForeColor = Color.White,
                DialogResult = DialogResult.None
            };
            validateButton.FlatAppearance.BorderSize = 0;
            validateButton.Click += OnValidateClick;

            AcceptButton = validateButton;

            Controls.Add(fileLabel);
            Controls.Add(captionLabel);
            Controls.Add(_captionBox);
            Controls.Add(trimLabel);
            Controls.Add(trimStartLabel);
            Controls.Add(_trimStartBox);
            Controls.Add(trimEndLabel);
            Controls.Add(_trimEndBox);
            Controls.Add(validateButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _captionBox.Focus();
        }

        private void OnValidateClick(object sender, EventArgs e)
        {
            Caption = _captionBox.Text;

            if (!string.IsNullOrWhiteSpace(_trimStartBox.Text))
            {
                double ts;
                if (double.TryParse(_trimStartBox.Text, out ts))
                    TrimStart = ts;
            }

            if (!string.IsNullOrWhiteSpace(_trimEndBox.Text))
            {
                double te;
                if (double.TryParse(_trimEndBox.Text, out te))
                    TrimEnd = te;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
