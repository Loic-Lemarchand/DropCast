using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DropCast
{
    /// <summary>
    /// Overlay that appears only while a real OLE drag-drop operation is in progress.
    /// Detection: polls for the <c>SysDragImage</c> window that Windows Shell creates
    /// whenever an OLE file drag is initiated from Explorer / Desktop.
    /// This window does NOT exist during regular left-click drags, so there are
    /// no false positives.
    /// </summary>
    public class DropOverlayForm : Form
    {
        // ── Palette ──
        private static readonly Color BgIdle = Color.FromArgb(13, 43, 62);
        private static readonly Color BgHover = Color.FromArgb(20, 61, 84);
        private static readonly Color Accent = Color.FromArgb(42, 191, 191);
        private static readonly Color AccentLight = Color.FromArgb(72, 209, 204);
        private static readonly Color BorderIdle = Color.FromArgb(30, 74, 95);

        private const int NormalSize = 70;
        private const int ExpandedSize = 130;
        private bool _isDragOver;
        private bool _handleReady;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_LBUTTON = 0x01;

        private readonly Timer _dragDetectTimer;
        private bool _dragTracking;

        public event EventHandler<string[]> FilesDropped;

        public DropOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AllowDrop = true;
            Size = new Size(NormalSize, NormalSize);
            BackColor = BgIdle;
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

        // ---- OLE drag detection via SysDragImage window ----

        /// <summary>
        /// Returns <c>true</c> when the Shell drag-image window exists, which means
        /// an OLE file drag from Explorer / Desktop is currently in progress.
        /// </summary>
        private static bool IsOleDragActive()
        {
            return FindWindow("SysDragImage", null) != IntPtr.Zero;
        }

        private void OnDragDetectTick(object sender, EventArgs e)
        {
            bool lbDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            if (_dragTracking)
            {
                // Keep showing until the mouse button is released
                if (!lbDown)
                {
                    _dragTracking = false;
                    if (Visible && !_isDragOver)
                        Visible = false;
                }
                return;
            }

            // Detect new OLE drag via SysDragImage window
            if (IsOleDragActive())
            {
                _dragTracking = true;
                if (!Visible)
                {
                    _isDragOver = false;
                    Size = new Size(NormalSize, NormalSize);
                    Opacity = 0.55;
                    PositionOnScreen(NormalSize);
                    Visible = true;
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
            _dragTracking = false;
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

            // Rounded background
            int r = 12;
            using (var path = RoundedRect(ClientRectangle, r))
            {
                using (var brush = new SolidBrush(_isDragOver ? BgHover : BgIdle))
                    g.FillPath(brush, path);

                using (var pen = new Pen(_isDragOver ? Accent : BorderIdle, 2))
                    g.DrawPath(pen, path);
            }

            // Dashed inner border when drag is over
            if (_isDragOver)
            {
                var inner = new Rectangle(6, 6, Width - 13, Height - 13);
                using (var path = RoundedRect(inner, 8))
                using (var pen = new Pen(AccentLight, 1) { DashStyle = DashStyle.Dash })
                {
                    g.DrawPath(pen, path);
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
                using (var brush = new SolidBrush(AccentLight))
                {
                    var textRect = new RectangleF(0, Height * 0.55f, Width, Height * 0.4f);
                    g.DrawString("Drop ici !", textFont, brush, textRect, sf);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _dragDetectTimer.Stop();
            _dragDetectTimer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
