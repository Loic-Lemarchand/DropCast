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

        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private CancellationTokenSource _mediaCts = new CancellationTokenSource();
        private static readonly HttpClient _httpClient = new HttpClient();
        private Stopwatch _vlcSw;

        public event EventHandler DisplayCompleted;

        private void OnDisplayCompleted()
        {
            DisplayCompleted?.Invoke(this, EventArgs.Empty);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public Form1()
        {
            Core.Initialize();
            _libVLC = new LibVLC(
                "--avcodec-skiploopfilter=4",
                "--clock-jitter=0",
                "--drop-late-frames",
                "--skip-frames",
                "--avcodec-hw=any"
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
        }

        // IMediaDisplay implementation
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
                    ResumeLayout(true);
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
                BeginInvoke(new Action(() => Caption.Visible = false));
                OnDisplayCompleted();
            };

            onError = (s, ea) =>
            {
                _mediaPlayer.EndReached -= onEnded;
                _mediaPlayer.EncounteredError -= onError;
                BeginInvoke(new Action(() => Caption.Visible = false));
                OnDisplayCompleted();
            };

            _mediaPlayer.EndReached += onEnded;
            _mediaPlayer.EncounteredError += onError;

            using (var media = new Media(_libVLC, new Uri(audioUrl)))
            {
                _mediaPlayer.Play(media);
            }
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
                        ResumeLayout(true);
                        pictureBox.Refresh();

                        Task.Delay(8000, token).ContinueWith(__ =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    pictureBox.Visible = false;
                                    Caption.Visible = false;
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
    }
}
