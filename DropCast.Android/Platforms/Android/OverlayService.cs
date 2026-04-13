using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using DropCast.Android.Models;
using DropCast.Android.Services;
using Microsoft.Extensions.Logging;
using Uri = Android.Net.Uri;
using View = Android.Views.View;
using Color = Android.Graphics.Color;

namespace DropCast.Android.Platforms;

[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback, Exported = false)]
public class OverlayService : Service
{
    private IWindowManager? _windowManager;
    private View? _overlayView;
    private VideoView? _videoView;
    private ImageView? _imageView;
    private MemeTextView? _captionView;
    private MemeTextView? _memeTextView;
    private TextView? _authorView;
    private FrameLayout? _container;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private bool _isShowingMedia;
    private string? _currentTempFile;

    // Injected via static — Android services don't support constructor DI easily
    public static DiscordService? Discord { get; set; }
    public static WhatsAppService? WhatsApp { get; set; }
    public static VideoResolver? Resolver { get; set; }
    public static ILogger<OverlayService>? Logger { get; set; }

    private const string ChannelId = "dropcast_overlay";
    private const int NotificationId = 9001;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification());
        _windowManager = GetSystemService(WindowService)?.JavaCast<IWindowManager>();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == "STOP")
        {
            Cleanup();
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (Discord != null)
            Discord.MessageReceived += OnMessageReceived;
        if (WhatsApp != null)
            WhatsApp.MessageReceived += OnMessageReceived;

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        Cleanup();
        base.OnDestroy();
    }

    private void Cleanup()
    {
        if (Discord != null)
            Discord.MessageReceived -= OnMessageReceived;
        if (WhatsApp != null)
            WhatsApp.MessageReceived -= OnMessageReceived;
        DismissOverlay();
    }

    private async void OnMessageReceived(object? sender, DropCastMessage message)
    {
        if (_isShowingMedia)
        {
            Logger?.LogInformation("⏳ Media already showing, skipping");
            return;
        }

        _isShowingMedia = true;
        try
        {
            await ProcessMessageAsync(message);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing message");
            _isShowingMedia = false;
        }
    }

    private async Task ProcessMessageAsync(DropCastMessage message)
    {
        TrimParser.TryParse(message.Text, out double? trimStart, out double? trimEnd);
        string caption = TrimParser.StripTrim(message.Caption ?? message.Text);

        // 1) Attachments (direct files from Discord)
        if (message.Attachments.Length > 0)
        {
            var first = message.Attachments[0];
            switch (first.Type)
            {
                case MediaType.Video:
                    _handler.Post(() => ShowVideoOverlay(first.Url, caption, message.AuthorName, trimStart, trimEnd));
                    return;
                case MediaType.Audio:
                    _handler.Post(() => ShowVideoOverlay(first.Url, caption, message.AuthorName, trimStart, trimEnd));
                    return;
                case MediaType.Image:
                    _ = ShowImageOverlayAsync(first.Url, caption, message.AuthorName);
                    return;
            }
        }

        // 2) URLs — resolve, download to temp, play locally
        var urls = UrlDetector.DetectUrls(message.Text);
        if (urls.Count > 0)
        {
            var target = urls[0];
            string urlCaption = UrlDetector.StripUrls(caption);

            // Direct media links — play/show directly (no download needed)
            if (target.Platform == "Direct")
            {
                string lower = target.OriginalUrl.ToLowerInvariant();
                if (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".webm") ||
                    lower.EndsWith(".mkv") || lower.EndsWith(".mp3") || lower.EndsWith(".wav"))
                {
                    _handler.Post(() => ShowVideoOverlay(target.OriginalUrl, urlCaption, message.AuthorName, trimStart, trimEnd));
                }
                else
                {
                    _ = ShowImageOverlayAsync(target.OriginalUrl, urlCaption, message.AuthorName);
                }
                return;
            }

            // Social platforms — resolve → conditionally download → play
            if (Resolver != null)
            {
                var resolved = await Resolver.ResolveAsync(target.OriginalUrl);
                if (resolved != null && !resolved.HasError)
                {
                    if (string.IsNullOrWhiteSpace(urlCaption))
                        urlCaption = resolved.Title ?? "";

                    if (resolved.NeedsDownload)
                    {
                        var tempFile = await Resolver.DownloadToTempAsync(resolved.DirectUrl, resolved.Referrer, resolved.UserAgent);
                        if (tempFile != null)
                        {
                            _currentTempFile = tempFile;
                            string cap = urlCaption;
                            _handler.Post(() => ShowVideoOverlay(tempFile, cap, message.AuthorName, trimStart, trimEnd));
                            return;
                        }
                        Logger?.LogWarning("⚠️ Download failed for: {Url}", resolved.DirectUrl);
                        _handler.Post(() => ShowTextOverlay("❌ Échec du téléchargement", message.AuthorName));
                        return;
                    }
                    else
                    {
                        string directUrl = resolved.DirectUrl;
                        // Track local temp files (from yt-dlp download) for cleanup
                        if (directUrl.StartsWith("/"))
                            _currentTempFile = directUrl;
                        string cap = urlCaption;
                        _handler.Post(() => ShowVideoOverlay(directUrl, cap, message.AuthorName, trimStart, trimEnd));
                        return;
                    }
                }

                string error = resolved?.Error ?? "❌ Impossible de résoudre le lien";
                _handler.Post(() => ShowTextOverlay(error, message.AuthorName));
                return;
            }

            _handler.Post(() => ShowTextOverlay("❌ Résolveur non disponible", message.AuthorName));
            return;
        }

        // 3) Plain text
        if (!string.IsNullOrWhiteSpace(caption))
        {
            _handler.Post(() => ShowTextOverlay(caption, message.AuthorName));
        }
        else
        {
            _isShowingMedia = false;
        }
    }

    private void EnsureOverlayView()
    {
        if (_overlayView != null) return;

        var context = ApplicationContext!;
        var dm = context.Resources!.DisplayMetrics!;
        int dip16 = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 16, dm);
        int dip24 = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 24, dm);
        int dip60 = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 60, dm);

        _container = new FrameLayout(context);
        _container.SetBackgroundColor(Color.ParseColor("#CC000000"));
        _container.Clickable = true;
        _container.Click += (s, e) => DismissOverlay();

        _videoView = new VideoView(context);
        _videoView.Visibility = ViewStates.Gone;
        _container.AddView(_videoView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _imageView = new ImageView(context);
        _imageView.Visibility = ViewStates.Gone;
        _imageView.SetScaleType(ImageView.ScaleType.FitCenter);
        _container.AddView(_imageView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Meme-style caption (bottom area, for captions under video/image)
        _captionView = new MemeTextView(context);
        _captionView.Visibility = ViewStates.Gone;
        _captionView.TextSize = 20;
        var captionParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom | GravityFlags.CenterHorizontal);
        captionParams.SetMargins(dip16, 0, dip16, dip60);
        _container.AddView(_captionView, captionParams);

        // Large meme text
        _memeTextView = new MemeTextView(context);
        _memeTextView.Visibility = ViewStates.Gone;
        _memeTextView.TextSize = 32;
        var memeParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Center);
        memeParams.SetMargins(dip24, 0, dip24, 0);
        _container.AddView(_memeTextView, memeParams);

        // Author name (small, very bottom)
        _authorView = new TextView(context);
        _authorView.SetTextColor(Color.ParseColor("#AAAAAA"));
        _authorView.TextSize = 11;
        _authorView.Gravity = GravityFlags.Center;
        var authorParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom | GravityFlags.CenterHorizontal);
        authorParams.SetMargins(dip16, 0, dip16, dip16);
        _container.AddView(_authorView, authorParams);

        _overlayView = _container;
    }

    private void AddOverlayToWindow()
    {
        if (_windowManager == null || _overlayView == null) return;
        if (_overlayView.Parent != null) return;

        float zoneLeft = AppSettings.OverlayZoneLeft;
        float zoneTop = AppSettings.OverlayZoneTop;
        float zoneWidth = AppSettings.OverlayZoneWidth;
        float zoneHeight = AppSettings.OverlayZoneHeight;

        WindowManagerLayoutParams layoutParams;

        if (zoneWidth >= 0.99f && zoneHeight >= 0.99f && zoneLeft <= 0.01f && zoneTop <= 0.01f)
        {
            // Full screen (default)
            layoutParams = new WindowManagerLayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent,
                WindowManagerTypes.ApplicationOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen,
                Format.Translucent);
        }
        else
        {
            var dm = ApplicationContext!.Resources!.DisplayMetrics!;
            int screenW = dm.WidthPixels;
            int screenH = dm.HeightPixels;
            int w = (int)(zoneWidth * screenW);
            int h = (int)(zoneHeight * screenH);
            int x = (int)(zoneLeft * screenW);
            int y = (int)(zoneTop * screenH);

            layoutParams = new WindowManagerLayoutParams(
                w, h,
                WindowManagerTypes.ApplicationOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Left,
                X = x,
                Y = y
            };
        }

        _windowManager.AddView(_overlayView, layoutParams);
    }

    private void ShowVideoOverlay(string url, string caption, string author, double? trimStart, double? trimEnd)
    {
        EnsureOverlayView();
        _imageView!.Visibility = ViewStates.Gone;
        _memeTextView!.Visibility = ViewStates.Gone;
        _videoView!.Visibility = ViewStates.Visible;
        _authorView!.Text = author;

        if (!string.IsNullOrWhiteSpace(caption))
        {
            _captionView!.Visibility = ViewStates.Visible;
            _captionView.Text = caption;
        }
        else
        {
            _captionView!.Visibility = ViewStates.Gone;
        }

        _videoView.SetOnPreparedListener(new MediaPreparedListener(trimStart, trimEnd, () =>
        {
            int duration = _videoView.Duration;
            if (duration > 0)
                _handler.PostDelayed(() => DismissOverlay(), duration + 1000);
            else
                _handler.PostDelayed(() => DismissOverlay(), 60000);
        }));
        _videoView.SetOnCompletionListener(new MediaCompletionListener(() => DismissOverlay()));
        _videoView.SetOnErrorListener(new MediaErrorListener(() =>
        {
            Logger?.LogWarning("⚠️ VideoView error for: {Url}", url);
            try { _videoView.StopPlayback(); } catch { }
            _videoView.Visibility = ViewStates.Gone;
            _memeTextView!.Visibility = ViewStates.Visible;
            _memeTextView.Text = "❌ Impossible de lire cette vidéo";
            _handler.RemoveCallbacksAndMessages(null);
            _handler.PostDelayed(() => DismissOverlay(), 5000);
        }));

        // Local temp file or remote URL
        if (url.StartsWith("/"))
            _videoView.SetVideoURI(Uri.FromFile(new Java.IO.File(url))!);
        else
            _videoView.SetVideoURI(Uri.Parse(url));

        AddOverlayToWindow();
        _videoView.Start();
    }

    private async Task ShowImageOverlayAsync(string url, string caption, string author)
    {
        try
        {
            using var http = new HttpClient();
            var data = await http.GetByteArrayAsync(url);
            var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length);
            if (bitmap == null) { _isShowingMedia = false; return; }

            _handler.Post(() =>
            {
                EnsureOverlayView();
                _videoView!.Visibility = ViewStates.Gone;
                _memeTextView!.Visibility = ViewStates.Gone;
                _imageView!.Visibility = ViewStates.Visible;
                _imageView.SetImageBitmap(bitmap);
                _authorView!.Text = author;

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    _captionView!.Visibility = ViewStates.Visible;
                    _captionView.Text = caption;
                }
                else
                {
                    _captionView!.Visibility = ViewStates.Gone;
                }

                AddOverlayToWindow();
                _handler.PostDelayed(() => DismissOverlay(), 10000);
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load image: {Url}", url);
            _isShowingMedia = false;
        }
    }

    private void ShowTextOverlay(string text, string author)
    {
        EnsureOverlayView();
        _videoView!.Visibility = ViewStates.Gone;
        _imageView!.Visibility = ViewStates.Gone;
        _captionView!.Visibility = ViewStates.Gone;
        _memeTextView!.Visibility = ViewStates.Visible;
        _memeTextView.Text = text;
        _authorView!.Text = author;
        AddOverlayToWindow();

        _handler.PostDelayed(() => DismissOverlay(), 8000);
    }

    private void DismissOverlay()
    {
        _handler.RemoveCallbacksAndMessages(null);

        if (_videoView != null)
        {
            try { _videoView.StopPlayback(); } catch { }
        }

        if (_overlayView?.Parent != null && _windowManager != null)
        {
            try { _windowManager.RemoveView(_overlayView); } catch { }
        }

        if (_captionView != null)
            _captionView.Visibility = ViewStates.Gone;
        if (_memeTextView != null)
            _memeTextView.Visibility = ViewStates.Gone;

        // Clean up temp file
        if (_currentTempFile != null)
        {
            try { File.Delete(_currentTempFile); } catch { }
            _currentTempFile = null;
        }

        _isShowingMedia = false;
    }

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "DropCast Overlay",
            NotificationImportance.Low)
        {
            Description = "DropCast media overlay service"
        };
        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var stopIntent = new Intent(this, typeof(OverlayService));
        stopIntent.SetAction("STOP");
        var stopPending = PendingIntent.GetService(this, 0, stopIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("DropCast")
            .SetContentText("En attente de memes...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .AddAction(new Notification.Action.Builder(
                null, "Arrêter", stopPending).Build())
            .Build()!;
    }

    // Helper listener classes for VideoView
    private class MediaPreparedListener(double? trimStart, double? trimEnd, Action onPrepared) : Java.Lang.Object, MediaPlayer.IOnPreparedListener
    {
        public void OnPrepared(MediaPlayer? mp)
        {
            if (mp == null) return;
            if (trimStart.HasValue)
                mp.SeekTo((int)(trimStart.Value * 1000));
            if (trimEnd.HasValue)
            {
                // Schedule stop at trim end
                int stopAt = (int)(trimEnd.Value * 1000) - (trimStart.HasValue ? (int)(trimStart.Value * 1000) : 0);
                new Handler(Looper.MainLooper!).PostDelayed(() =>
                {
                    try { mp.Stop(); } catch { }
                }, Math.Max(stopAt, 1000));
            }
            onPrepared();
        }
    }

    private class MediaCompletionListener(Action onComplete) : Java.Lang.Object, MediaPlayer.IOnCompletionListener
    {
        public void OnCompletion(MediaPlayer? mp) => onComplete();
    }

    private class MediaErrorListener(Action onError) : Java.Lang.Object, MediaPlayer.IOnErrorListener
    {
        public bool OnError(MediaPlayer? mp, MediaError what, int extra)
        {
            onError();
            return true;
        }
    }
}
