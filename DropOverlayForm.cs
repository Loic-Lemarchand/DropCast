using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DropCast
{
    /// <summary>
    /// Overlay that appears only while the user is dragging something (detected via
    /// <c>GetAsyncKeyState</c> + cursor movement polling). Accepts file drops from
    /// Windows Explorer / Desktop, then fires <see cref="FilesDropped"/>.
    /// </summary>
    public class DropOverlayForm : Form
    {
        private const int NormalSize = 70;
        private const int ExpandedSize = 130;
        private bool _isDragOver;

        // --- drag-detection polling ---
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_LBUTTON = 0x01;

        private readonly Timer _dragDetectTimer;
        private int _dragMoveTicks;
        private Point _lastCursorPos;
        private bool _handleReady;

        public event EventHandler<string[]> FilesDropped;

        public DropOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AllowDrop = true;
            Size = new Size(NormalSize, NormalSize);
            BackColor = Color.FromArgb(40, 40, 40);
            Opacity = 0.55;
            DoubleBuffered = true;

            PositionOnScreen(NormalSize);

            DragEnter += OnDragEnter;
            DragLeave += OnDragLeave;
            DragDrop += OnDragDrop;

            _dragDetectTimer = new Timer { Interval = 80 };
            _dragDetectTimer.Tick += OnDragDetectTick;
            _dragDetectTimer.Start();
        }

        /// <summary>
        /// Suppress the very first Show() so the form is never displayed at startup,
        /// while still creating the native handle (needed for Timer and BeginInvoke).
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            if (!_handleReady)
            {
                _handleReady = true;
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — hide from Alt+Tab
                return cp;
            }
        }

        // ---- drag-detection via polling ----

        private void OnDragDetectTick(object sender, EventArgs e)
        {
            bool lbDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            if (lbDown)
            {
                Point cur = Cursor.Position;
                if (cur != _lastCursorPos)
                {
                    _dragMoveTicks++;
                    _lastCursorPos = cur;
                }

                // ~320 ms of continuous dragging → show the overlay
                if (_dragMoveTicks >= 4 && !Visible)
                {
                    _isDragOver = false;
                    Size = new Size(NormalSize, NormalSize);
                    Opacity = 0.55;
                    PositionOnScreen(NormalSize);
                    Visible = true;
                }
            }
            else
            {
                _dragMoveTicks = 0;
                if (Visible && !_isDragOver)
                {
                    Visible = false;
                }
            }
        }

        // ---- OLE drag-drop events ----

        private void PositionOnScreen(int size)
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Left + 20, screen.Bottom - size - 20);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _isDragOver = true;
                Size = new Size(ExpandedSize, ExpandedSize);
                Opacity = 0.95;
                PositionOnScreen(ExpandedSize);
                Invalidate();
            }
        }

        private void OnDragLeave(object sender, EventArgs e)
        {
            _isDragOver = false;
            Size = new Size(NormalSize, NormalSize);
            Opacity = 0.55;
            PositionOnScreen(NormalSize);
            Invalidate();
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            _isDragOver = false;
            Visible = false;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    // Defer so the OLE drag operation completes before we show a modal dialog
                    BeginInvoke(new Action(() => FilesDropped?.Invoke(this, files)));
                }
            }
        }

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Background
            using (var brush = new SolidBrush(_isDragOver ? Color.FromArgb(40, 80, 160) : Color.FromArgb(40, 40, 40)))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            // Border
            using (var pen = new Pen(_isDragOver ? Color.FromArgb(100, 180, 255) : Color.FromArgb(80, 80, 80), 2))
            {
                g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            }

            // Dashed inner border when drag is over
            if (_isDragOver)
            {
                using (var pen = new Pen(Color.FromArgb(150, 200, 255), 1) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(pen, 6, 6, Width - 13, Height - 13);
                }
            }

            // Icon + text
            string icon = "📂";
            using (var iconFont = new Font("Segoe UI Emoji", _isDragOver ? 24F : 20F))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var iconRect = _isDragOver
                    ? new RectangleF(0, 0, Width, Height * 0.6f)
                    : new RectangleF(0, 0, Width, Height);
                g.DrawString(icon, iconFont, Brushes.White, iconRect, sf);
            }

            if (_isDragOver)
            {
                using (var textFont = new Font("Segoe UI", 11F, FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    var textRect = new RectangleF(0, Height * 0.55f, Width, Height * 0.4f);
                    g.DrawString("Drop ici !", textFont, Brushes.White, textRect, sf);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _dragDetectTimer.Stop();
            _dragDetectTimer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
