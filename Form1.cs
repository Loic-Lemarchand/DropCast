using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;

namespace DropCast
{
    public partial class Form1 : Form
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE;

        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private CancellationTokenSource _mediaCts = new CancellationTokenSource();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public Form1()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
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

        public void DisplayVideo(string videoUrl, string captionText)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string>(DisplayVideo), videoUrl, captionText);
                return;
            }

            CancelPreviousMedia();
            var token = _mediaCts.Token;

            videoView.Visible = false;
            Caption.Visible = false;

            AdjustMediaLayout(videoView);

            EventHandler<EventArgs> onPlaying = null;
            EventHandler<EventArgs> onEnded = null;
            EventHandler<EventArgs> onError = null;

            onPlaying = (s, ea) =>
            {
                _mediaPlayer.Playing -= onPlaying;
                BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested) return;
                    videoView.Visible = true;
                    Caption.Text = captionText;
                    Caption.Width = videoView.Width;
                    Caption.MaximumSize = new Size(videoView.Width, 0);
                    AdjustCaptionHeight();
                    Caption.SetBounds(videoView.Left, videoView.Bottom + 10, videoView.Width, Caption.Height);
                    Caption.Visible = true;
                    Caption.BringToFront();
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
            };

            _mediaPlayer.Playing += onPlaying;
            _mediaPlayer.EndReached += onEnded;
            _mediaPlayer.EncounteredError += onError;

            using (var media = new Media(_libVLC, new Uri(videoUrl)))
            {
                _mediaPlayer.Play(media);
            }

            Task.Delay(20000, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    // Stop must not be called from a VLC event thread
                    ThreadPool.QueueUserWorkItem(__ => _mediaPlayer.Stop());
                    BeginInvoke(new Action(() =>
                    {
                        videoView.Visible = false;
                        Caption.Visible = false;
                    }));
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public void PlayAudio(string audioUrl, string captionText)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string>(PlayAudio), audioUrl, captionText);
                return;
            }

            CancelPreviousMedia();
            var token = _mediaCts.Token;

            videoView.Visible = false;

            Caption.Text = captionText;
            Caption.Width = ClientSize.Width / 2;
            Caption.SetBounds((ClientSize.Width - Caption.Width) / 2, ClientSize.Height - 100, Caption.Width, 0);
            Caption.Visible = true;
            Caption.AdjustHeight();
            Caption.BringToFront();

            using (var media = new Media(_libVLC, new Uri(audioUrl)))
            {
                _mediaPlayer.Play(media);
            }

            Task.Delay(15000, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    ThreadPool.QueueUserWorkItem(__ => _mediaPlayer.Stop());
                    BeginInvoke(new Action(() =>
                    {
                        Caption.Visible = false;
                    }));
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
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

            Task.Run(() =>
            {
                try
                {
                    Image imgTemp;
                    using (var wc = new WebClient())
                    {
                        byte[] imageBytes = wc.DownloadData(imageUrl);
                        // Do not dispose the MemoryStream; Image.FromStream requires it to stay open
                        var ms = new MemoryStream(imageBytes);
                        imgTemp = Image.FromStream(ms);
                    }

                    BeginInvoke(new Action(() =>
                    {
                        if (imgTemp == null || token.IsCancellationRequested) return;

                        int screenWidth = ClientSize.Width;
                        int screenHeight = ClientSize.Height;
                        int maxWidth = (int)(screenWidth * 0.6);
                        int maxHeight = (int)(screenHeight * 0.6);

                        double aspectRatio = (double)imgTemp.Width / imgTemp.Height;
                        int newWidth = maxWidth;
                        int newHeight = (int)(maxWidth / aspectRatio);

                        if (newHeight > maxHeight)
                        {
                            newHeight = maxHeight;
                            newWidth = (int)(maxHeight * aspectRatio);
                        }

                        int posX = (screenWidth - newWidth) / 2;
                        int posY = 50;

                        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
                        pictureBox.SetBounds(posX, posY, newWidth, newHeight);
                        pictureBox.Image = imgTemp;
                        pictureBox.Visible = true;
                        pictureBox.Refresh();

                        Caption.Text = captionText;
                        Caption.Width = pictureBox.Width;
                        Caption.MaximumSize = new Size(pictureBox.Width, 0);
                        AdjustCaptionHeight();
                        Caption.SetBounds(pictureBox.Left, pictureBox.Bottom + 10, pictureBox.Width, Caption.Height);
                        Caption.Visible = true;
                        Caption.BringToFront();

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
