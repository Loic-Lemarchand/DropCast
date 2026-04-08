using DropCast.Abstractions;
using DropCast.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DropCast
{
    /// <summary>
    /// Central pipeline that receives messages from any <see cref="IMessageSource"/>,
    /// detects URLs (YouTube, TikTok, Instagram, etc.), resolves them via YoutubeExplode / OpenGraph,
    /// and dispatches media to the <see cref="IMediaDisplay"/>.
    /// </summary>
    public class MessagePipeline
    {
        private readonly ILogger _logger;
        private readonly VideoResolver _videoResolver;
        private readonly IMediaDisplay _display;
        private readonly List<IMessageSource> _sources = new List<IMessageSource>();

        public MessagePipeline(ILogger<MessagePipeline> logger, VideoResolver videoResolver, IMediaDisplay display)
        {
            _logger = logger;
            _videoResolver = videoResolver;
            _display = display;
        }

        public void RegisterSource(IMessageSource source)
        {
            source.MessageReceived += OnMessageReceived;
            _sources.Add(source);
            _logger.LogInformation("✅ Registered message source: {Platform}", source.PlatformName);
        }

        public async Task ConnectAllAsync()
        {
            foreach (var source in _sources)
            {
                try
                {
                    await source.ConnectAsync();
                    _logger.LogInformation("🟢 Connected to {Platform}", source.PlatformName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to connect to {Platform}", source.PlatformName);
                }
            }
        }

        public async Task DisconnectAllAsync()
        {
            foreach (var source in _sources)
            {
                try
                {
                    await source.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to disconnect from {Platform}", source.PlatformName);
                }
            }
        }

        private async void OnMessageReceived(object sender, DropCastMessage message)
        {
            try
            {
                await ProcessMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from {Platform}", message.SourcePlatform);
            }
        }

        private async Task ProcessMessageAsync(DropCastMessage message)
        {
            // Parse optional trim syntax [start-end] from the message text
            double? trimStart = null;
            double? trimEnd = null;
            TrimParser.TryParse(message.Text, out trimStart, out trimEnd);

            // Strip trim syntax from caption so it doesn't display on screen
            string caption = TrimParser.StripTrim(message.Caption ?? message.Text);

            // 1) File attachments have priority
            if (message.Attachments != null && message.Attachments.Length > 0)
            {
                bool hasTimedMedia = message.Attachments.Any(a =>
                    a.Type == MediaType.Video || a.Type == MediaType.Audio || a.Type == MediaType.Image);

                if (!hasTimedMedia)
                {
                    _display.ShowText(caption);
                }

                var first = message.Attachments[0];
                switch (first.Type)
                {
                    case MediaType.Video:
                        _display.ShowVideo(first.Url, caption, trimStart, trimEnd);
                        return;
                    case MediaType.Audio:
                        _display.PlayAudio(first.Url, caption);
                        return;
                    case MediaType.Image:
                        _display.ShowImage(first.Url, caption);
                        return;
                }
            }

            // 2) Check for platform URLs in the text (YouTube, TikTok, Instagram, etc.)
            var detectedUrls = UrlDetector.DetectUrls(message.Text);
            if (detectedUrls.Count > 0)
            {
                var target = detectedUrls[0];
                // Strip both URLs and trim syntax from caption
                string urlCaption = UrlDetector.StripUrls(caption);

                _logger.LogInformation("🔗 Detected {Platform} URL: {Url}", target.Platform, target.OriginalUrl);

                if (trimStart.HasValue || trimEnd.HasValue)
                    _logger.LogInformation("✂️ Trim requested: [{Start}-{End}]",
                        trimStart.HasValue ? trimStart.Value.ToString("F0") : "",
                        trimEnd.HasValue ? trimEnd.Value.ToString("F0") : "");

                // Direct media links — play directly, no resolution needed
                if (target.Platform == "Direct")
                {
                    string lower = target.OriginalUrl.ToLower();
                    if (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".webm") ||
                        lower.EndsWith(".avi") || lower.EndsWith(".mkv"))
                    {
                        _display.ShowVideo(target.OriginalUrl, urlCaption, trimStart, trimEnd);
                    }
                    else if (lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".ogg") ||
                             lower.EndsWith(".flac"))
                    {
                        _display.PlayAudio(target.OriginalUrl, urlCaption);
                    }
                    else
                    {
                        _display.ShowImage(target.OriginalUrl, urlCaption);
                    }
                    return;
                }

                // Resolve via YoutubeExplode (YouTube) or OpenGraph (TikTok, Instagram, Twitter, Reddit)
                if (_videoResolver.IsAvailable)
                {
                    var resolved = await _videoResolver.ResolveAsync(target.OriginalUrl);
                    if (resolved != null)
                    {
                        if (resolved.HasError)
                        {
                            _logger.LogWarning("⚠️ {Error} — {Url}", resolved.Error, target.OriginalUrl);
                            _display.ShowText(resolved.Error);
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(urlCaption))
                            urlCaption = resolved.Title ?? "";

                        _logger.LogInformation("▶️ Playing resolved video: {Title}", resolved.Title);
                        _display.ShowVideo(resolved.DirectUrl, urlCaption, trimStart, trimEnd);
                        return;
                    }
                }

                _logger.LogWarning("⚠️ Could not resolve URL: {Url}", target.OriginalUrl);
                _display.ShowText("❌ Impossible de résoudre le lien");
            }

            // 3) Plain text message
            if (!string.IsNullOrWhiteSpace(caption))
            {
                _display.ShowText(caption);
            }
        }
    }
}
