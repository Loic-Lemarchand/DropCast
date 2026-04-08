using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using DropCast.Abstractions;

namespace DropCast
{
    public partial class Form1 : Form, IMediaDisplay
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_VOLUME = 9001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

        private const int WM_LBUTTONDOWN = 0x0201;

        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private CancellationTokenSource _mediaCts = new CancellationTokenSource();
        private static readonly HttpClient _httpClient = new HttpClient();
        private Stopwatch _vlcSw;
        private ControlPanel _controlPanel;
        private bool _clickToDismissEnabled;
        private bool _showAuthorInfo;
        private bool _mediaActive;
        private string _currentAuthorName;
        private Image _cachedAvatar;
        private PictureBox _authorAvatar;
        private Label _authorLabel;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private LowLevelMouseProc _mouseHookProc;

        public event EventHandler DisplayCompleted;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public Form1()
        {
            Core.Initialize();
            _libVLC = new LibVLC(
                "--avcodec-skiploopfilter=4",
                "--clock-jitter=0",
                "--drop-late-frames",
                "--skip-frames",
                "--avcodec-hw=any",
                "--audio-filter=compressor",
                "--compressor-rms-peak=0",
                "--compressor-attack=1.5",
                "--compressor-release=300",
                "--compressor-threshold=-20",
                "--compressor-ratio=20",
                "--compressor-knee=1",
                "--compressor-makeup-gain=12"
            );
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

            InitializeComponent();

            videoView.MediaPlayer = _mediaPlayer;
            _mediaCts = new CancellationTokenSource();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return cp;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            AllowTransparency = true;
            Opacity = 1;

            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, TOPMOST_FLAGS);

            videoView.Visible = false;

            Caption.Width = ClientSize.Width / 2;
            Caption.Height = 50;
            Caption.Location = new Point((ClientSize.Width - Caption.Width) / 2, 100);
            Caption.Visible = true;
            Caption.BringToFront();
            Caption.MaximumSize = new Size(ClientSize.Width, 0);
            Caption.AutoSize = false;
            Caption.TextAlign = ContentAlignment.MiddleCenter;
            Caption.BackColor = Color.Transparent;
            Caption.Parent = this;

            pictureBox.BackColor = Color.Transparent;
            pictureBox.Parent = this;

            // Author info overlay
            _authorAvatar = new PictureBox
            {
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false,
                BackColor = Color.Transparent,
                Parent = this
            };
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(0, 0, 40, 40);
                _authorAvatar.Region = new Region(path);
            }

            _authorLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Visible = false,
                Parent = this
            };

            // Control panel — wire events first, then apply saved settings
            var settings = UserSettings.Load();
            _controlPanel = new ControlPanel();
            _controlPanel.VolumeChanged += (s, vol) => _mediaPlayer.Volume = vol;
            _controlPanel.ClickToDismissChanged += (s, enabled) => _clickToDismissEnabled = enabled;
            _controlPanel.ShowAuthorInfoChanged += (s, enabled) => _showAuthorInfo = enabled;
            _controlPanel.Volume = settings.Volume;
            _controlPanel.ClickToDismissEnabled = settings.ClickToDismissEnabled;
            _controlPanel.ShowAuthorInfoEnabled = settings.ShowAuthorInfoEnabled;
            _controlPanel.PositionOnScreen();

            // Global hotkey: Ctrl+Shift+V
            RegisterHotKey(Handle, HOTKEY_ID_VOLUME, MOD_CONTROL | MOD_SHIFT, (int)Keys.V);

            // Low-level mouse hook for click-to-dismiss (catches clicks even on VLC DirectX surface)
            _mouseHookProc = MouseHookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void OnDisplayCompleted()
        {
            DisplayCompleted?.Invoke(this, EventArgs.Empty);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_VOLUME)
            {
                ToggleControlPanel();
            }
            base.WndProc(ref m);
        }

        private void ToggleControlPanel()
        {
            if (_controlPanel == null) return;

            if (_controlPanel.Visible)
            {
                _controlPanel.Hide();
            }
            else
            {
                _controlPanel.Volume = _mediaPlayer.Volume;
                _controlPanel.PositionOnScreen();
                _controlPanel.Show();
                _controlPanel.Activate();
            }
        }

        // IMediaDisplay implementation
        public void SetAuthorInfo(string authorName, string avatarUrl)
        {
            _currentAuthorName = authorName;
            var old = _cachedAvatar;
            _cachedAvatar = null;
            old?.Dispose();

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                try
                {
                    var bytes = _httpClient.GetByteArrayAsync(avatarUrl).GetAwaiter().GetResult();
                    _cachedAvatar = Image.FromStream(new MemoryStream(bytes));
                }
                catch { }
            }
        }

        public void ShowText(string message)
        {
            DisplayMessage(message, false);
        }

        public void DisplayMessage(string message, bool hasTimedMedia)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(DisplayMessage), message, hasTimedMedia);
                return;
            }

            Caption.Visible = true;
            Caption.BringToFront();
            Caption.Text = message;

            if (Caption.Width == 0)
                Caption.Width = ClientSize.Width / 2;

            Caption.UpdateSize(Caption.Width);
            Caption.AdjustHeight();

            if (!hasTimedMedia)
            {
                Task.Delay(5000).ContinueWith(_ =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => Caption.Visible = false));
                    else
                        Caption.Visible = false;
                });
            }
        }

        // IMediaDisplay.ShowImage
        void IMediaDisplay.ShowImage(string imageUrl, string caption)
        {
            DisplayImage(imageUrl, caption);
        }

        // IMediaDisplay.ShowVideo
        void IMediaDisplay.ShowVideo(string videoUrl, string caption, double? trimStartSeconds, double? trimEndSeconds, string referrer, string userAgent)
        {
            DisplayVideo(videoUrl, caption, trimStartSeconds, trimEndSeconds, referrer, userAgent);
        }

        // IMediaDisplay.PlayAudio
        void IMediaDisplay.PlayAudio(string audioUrl, string caption)
        {
            PlayAudio(audioUrl, caption);
        }

        public void DisplayVideo(string videoUrl, string captionText, double? trimStart = null, double? trimEnd = null, string referrer = null, string userAgent = null)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string, double?, double?, string, string>(DisplayVideo), videoUrl, captionText, trimStart, trimEnd, referrer, userAgent);
                return;
            }

            CancelPreviousMedia();
            var token = _mediaCts.Token;

            videoView.Visible = false;
            Caption.Visible = false;

            AdjustMediaLayout(videoView);

            // Pre-compute caption layout while media buffers
            Caption.Text = captionText;
            Caption.Width = videoView.Width;
            Caption.MaximumSize = new Size(videoView.Width, 0);
            AdjustCaptionHeight();
            Caption.SetBounds(videoView.Left, videoView.Bottom + 10, videoView.Width, Caption.Height);

            EventHandler<EventArgs> onPlaying = null;
            EventHandler<EventArgs> onEnded = null;
            EventHandler<EventArgs> onError = null;

            onPlaying = (s, ea) =>
            {
                _mediaPlayer.Playing -= onPlaying;
                if (_vlcSw != null)
                {
                    Debug.WriteLine(string.Format("⏱️ VLC startup: {0}ms", _vlcSw.ElapsedMilliseconds));
                    _vlcSw = null;
                }
                BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested) return;

                    SuspendLayout();
                    videoView.Visible = true;
                    Caption.Visible = true;
                    Caption.BringToFront();
                    ShowAuthorOverlay(videoView);
                    ResumeLayout(true);
                    _mediaActive = true;
                }));
            };

            onEnded = (s, ea) =>
            {
                _mediaPlayer.EndReached -= onEnded;
                _mediaPlayer.EncounteredError -= onError;
                BeginInvoke(new Action(() =>
                {
                    videoView.Visible = false;
                    Caption.Visible = false;
                    HideAuthorOverlay();
                    _mediaActive = false;
                }));
                OnDisplayCompleted();
            };

            onError = (s, ea) =>
            {
                _mediaPlayer.EndReached -= onEnded;
                _mediaPlayer.EncounteredError -= onError;
                BeginInvoke(new Action(() =>
                {
                    videoView.Visible = false;
                    Caption.Visible = false;
                    HideAuthorOverlay();
                    _mediaActive = false;
                }));
                OnDisplayCompleted();
            };

            _mediaPlayer.Playing += onPlaying;
            _mediaPlayer.EndReached += onEnded;
            _mediaPlayer.EncounteredError += onError;

            using (var media = new Media(_libVLC, new Uri(videoUrl)))
            {
                // Adapter le buffering selon la source
                if (videoUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    media.AddOption(":file-caching=0");
                }
                else
                {
                    media.AddOption(":network-caching=100");
                    media.AddOption(":file-caching=100");
                    media.AddOption(":live-caching=100");

                    if (!string.IsNullOrEmpty(referrer))
                        media.AddOption(string.Format(":http-referrer={0}", referrer));
                    if (!string.IsNullOrEmpty(userAgent))
                        media.AddOption(string.Format(":http-user-agent={0}", userAgent));
                }

                if (trimStart.HasValue && trimStart.Value > 0)
                    media.AddOption(string.Format(":start-time={0:F2}", trimStart.Value));
                if (trimEnd.HasValue && trimEnd.Value > 0)
                    media.AddOption(string.Format(":stop-time={0:F2}", trimEnd.Value));

                _vlcSw = Stopwatch.StartNew();
                _mediaPlayer.Play(media);
            }
        }

        public void PlayAudio(string audioUrl, string captionText)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string>(PlayAudio), audioUrl, captionText);
                return;
            }

            CancelPreviousMedia();

            videoView.Visible = false;

            Caption.Text = captionText;
            Caption.Width = ClientSize.Width / 2;
            Caption.SetBounds((ClientSize.Width - Caption.Width) / 2, ClientSize.Height - 100, Caption.Width, 0);
            Caption.Visible = true;
            Caption.AdjustHeight();
            Caption.BringToFront();

            EventHandler<EventArgs> onEnded = null;
            EventHandler<EventArgs> onError = null;

            onEnded = (s, ea) =>
            {
                _mediaPlayer.EndReached -= onEnded;
                _mediaPlayer.EncounteredError -= onError;
                BeginInvoke(new Action(() =>
                {
                    Caption.Visible = false;
                    HideAuthorOverlay();
                    _mediaActive = false;
                }));
                OnDisplayCompleted();
            };

            onError = (s, ea) =>
            {
                _mediaPlayer.EndReached -= onEnded;
                _mediaPlayer.EncounteredError -= onError;
                BeginInvoke(new Action(() =>
                {
                    Caption.Visible = false;
                    HideAuthorOverlay();
                    _mediaActive = false;
                }));
                OnDisplayCompleted();
            };

            _mediaPlayer.EndReached += onEnded;
            _mediaPlayer.EncounteredError += onError;

            using (var media = new Media(_libVLC, new Uri(audioUrl)))
            {
                _mediaPlayer.Play(media);
            }

            ShowAuthorOverlay(Caption);
            _mediaActive = true;
        }

        public void DisplayImage(string imageUrl, string captionText)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string>(DisplayImage), imageUrl, captionText);
                return;
            }

            CancelPreviousMedia();
            var token = _mediaCts.Token;

            // Ensure both are hidden while downloading
            pictureBox.Visible = false;
            Caption.Visible = false;

            Task.Run(async () =>
            {
                try
                {
                    Image imgTemp;
                    byte[] imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                    // Do not dispose the MemoryStream; Image.FromStream requires it to stay open
                    var ms = new MemoryStream(imageBytes);
                    imgTemp = Image.FromStream(ms);

                    BeginInvoke(new Action(() =>
                    {
                        if (imgTemp == null || token.IsCancellationRequested) return;

                        int sw = ClientSize.Width;
                        int sh = ClientSize.Height;
                        int mw = (int)(sw * 0.6);
                        int mh = (int)(sh * 0.6);

                        double aspectRatio = (double)imgTemp.Width / imgTemp.Height;
                        int newWidth = mw;
                        int newHeight = (int)(mw / aspectRatio);

                        if (newHeight > mh)
                        {
                            newHeight = mh;
                            newWidth = (int)(mh * aspectRatio);
                        }

                        int posX = (sw - newWidth) / 2;
                        int posY = 50;

                        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
                        pictureBox.SetBounds(posX, posY, newWidth, newHeight);
                        pictureBox.Image = imgTemp;

                        Caption.Text = captionText;
                        Caption.Width = pictureBox.Width;
                        Caption.MaximumSize = new Size(pictureBox.Width, 0);
                        AdjustCaptionHeight();
                        Caption.SetBounds(pictureBox.Left, pictureBox.Bottom + 10, pictureBox.Width, Caption.Height);

                        // Show both simultaneously using SuspendLayout for atomic update
                        SuspendLayout();
                        pictureBox.Visible = true;
                        Caption.Visible = true;
                        pictureBox.BringToFront();
                        Caption.BringToFront();
                        ShowAuthorOverlay(pictureBox);
                        ResumeLayout(true);
                        pictureBox.Refresh();
                        _mediaActive = true;

                        Task.Delay(8000, token).ContinueWith(__ =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    pictureBox.Visible = false;
                                    Caption.Visible = false;
                                    HideAuthorOverlay();
                                    _mediaActive = false;
                                    pictureBox.Image?.Dispose();
                                }));
                                OnDisplayCompleted();
                            }
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    }));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Erreur chargement image : " + ex.Message);
                }
            });
        }

        private void CancelPreviousMedia()
        {
            _mediaCts?.Cancel();
            _mediaCts?.Dispose();
            _mediaCts = new CancellationTokenSource();

            // Stop must not be called from a VLC event thread, but here we are on the UI thread
            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Stop();

            videoView.Visible = false;
            pictureBox.Visible = false;
            Caption.Visible = false;
            HideAuthorOverlay();
            _mediaActive = false;
        }

        private void AdjustMediaLayout(Control mediaControl)
        {
            int screenWidth = ClientSize.Width;
            int screenHeight = ClientSize.Height;

            float mediaHeightPct = 0.6f;
            float captionHeightPct = 0.1f;

            int captionHeight = (int)(screenHeight * captionHeightPct);
            int availableHeight = screenHeight - captionHeight;

            // Default to 16:9 aspect ratio
            double aspectRatio = 16.0 / 9.0;
            int mediaHeight = (int)(availableHeight * mediaHeightPct);
            int mediaWidth = (int)(mediaHeight * aspectRatio);

            if (mediaWidth > screenWidth * 0.6)
            {
                mediaWidth = (int)(screenWidth * 0.6);
                mediaHeight = (int)(mediaWidth / aspectRatio);
            }

            int marginLeft = (screenWidth - mediaWidth) / 2;
            int marginTop = 50;

            mediaControl.SetBounds(marginLeft, marginTop, mediaWidth, mediaHeight);

            Caption.UpdateSize(mediaWidth);
            Caption.SetBounds(marginLeft, marginTop + mediaHeight, mediaWidth, captionHeight);
        }

        private void AdjustCaptionHeight()
        {
            using (Graphics g = CreateGraphics())
            {
                Size textSize = TextRenderer.MeasureText(Caption.Text, Caption.Font, new Size(Caption.Width, int.MaxValue), TextFormatFlags.WordBreak);
                Caption.Height = textSize.Height;
            }
        }

        private void ShowAuthorOverlay(Control mediaControl)
        {
            if (!_showAuthorInfo || string.IsNullOrEmpty(_currentAuthorName)) return;

            int y = Caption.Bottom + 5;
            int x = mediaControl.Left;

            if (_cachedAvatar != null)
            {
                _authorAvatar.Image = _cachedAvatar;
                _authorAvatar.SetBounds(x, y, 40, 40);
                _authorAvatar.Visible = true;
                _authorAvatar.BringToFront();

                _authorLabel.Text = _currentAuthorName;
                _authorLabel.Location = new Point(x + 48, y + 8);
            }
            else
            {
                _authorLabel.Text = _currentAuthorName;
                _authorLabel.Location = new Point(x, y);
            }

            _authorLabel.Visible = true;
            _authorLabel.BringToFront();
        }

        private void HideAuthorOverlay()
        {
            if (_authorAvatar != null) _authorAvatar.Visible = false;
            if (_authorLabel != null) _authorLabel.Visible = false;
        }

        private void DismissCurrentMedia()
        {
            CancelPreviousMedia();
            OnDisplayCompleted();
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN && _clickToDismissEnabled && _mediaActive)
            {
                var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                Point clientPt = PointToClient(hookStruct.pt);

                bool hit = (videoView.Visible && videoView.Bounds.Contains(clientPt)) ||
                           (pictureBox.Visible && pictureBox.Bounds.Contains(clientPt)) ||
                           (Caption.Visible && Caption.Bounds.Contains(clientPt)) ||
                           (_authorAvatar != null && _authorAvatar.Visible && _authorAvatar.Bounds.Contains(clientPt)) ||
                           (_authorLabel != null && _authorLabel.Visible && _authorLabel.Bounds.Contains(clientPt));

                if (hit)
                {
                    BeginInvoke(new Action(DismissCurrentMedia));
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            new UserSettings
            {
                Volume = _controlPanel?.Volume ?? 100,
                ClickToDismissEnabled = _controlPanel?.ClickToDismissEnabled ?? false,
                ShowAuthorInfoEnabled = _controlPanel?.ShowAuthorInfoEnabled ?? false
            }.Save();

            if (_mouseHookHandle != IntPtr.Zero)
                UnhookWindowsHookEx(_mouseHookHandle);
            UnregisterHotKey(Handle, HOTKEY_ID_VOLUME);
            _controlPanel?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
