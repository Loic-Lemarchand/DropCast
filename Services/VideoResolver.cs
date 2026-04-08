using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace DropCast.Services
{
    /// <summary>
    /// Resolves video URLs from platforms to direct playable URLs.
    /// - YouTube : via YoutubeExplode (pure .NET)
    /// - TikTok, Instagram, Twitter/X, Reddit : via OpenGraph meta tag extraction (HTTP)
    /// Aucun exe externe requis (ni yt-dlp, ni ffmpeg).
    /// </summary>
    public class VideoResolver
    {
        private readonly ILogger<VideoResolver> _logger;
        private readonly YoutubeClient _youtube;
        private readonly HttpClient _http;

        private static readonly Regex OgVideoRegex = new Regex(
            @"<meta\s[^>]*property\s*=\s*[""']og:video(?::url)?[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex OgVideoRev = new Regex(
            @"<meta\s[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*property\s*=\s*[""']og:video(?::url)?[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex OgTitleRegex = new Regex(
            @"<meta\s[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex OgTitleRev = new Regex(
            @"<meta\s[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*property\s*=\s*[""']og:title[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public bool IsAvailable => true;

        public VideoResolver(ILogger<VideoResolver> logger)
        {
            _logger = logger;
            _youtube = new YoutubeClient();

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }

        /// <summary>
        /// <summary>
        /// Resolves any supported platform URL to a direct playable video URL.
        /// Returns null only on unrecoverable errors. Check <see cref="ResolvedMedia.Error"/> for user-facing messages.
        /// </summary>
        public async Task<ResolvedMedia> ResolveAsync(string url)
        {
            try
            {
                if (IsYouTubeUrl(url))
                    return await ResolveYouTubeAsync(url);

                return await ResolveViaOpenGraphAsync(url);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid URL format: {Url}", url);
                return ResolvedMedia.WithError(url, "❌ URL invalide : " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve URL: {Url}", url);
                return ResolvedMedia.WithError(url, "❌ Impossible de lire cette vidéo");
            }
        }

        private async Task<ResolvedMedia> ResolveYouTubeAsync(string url)
        {
            _logger.LogInformation("🎬 Resolving YouTube URL: {Url}", url);

            string videoTitle = null;
            TimeSpan? videoDuration = null;

            // Step 1: Get video metadata
            try
            {
                var video = await _youtube.Videos.GetAsync(url);
                videoTitle = video.Title;
                videoDuration = video.Duration;

                // Step 2: Try to get stream manifest
                var manifest = await _youtube.Videos.Streams.GetManifestAsync(video.Id);

                var muxed = manifest.GetMuxedStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();

                if (muxed != null)
                {
                    _logger.LogInformation("✅ YouTube resolved: {Title} ({Quality})", video.Title, muxed.VideoQuality);
                    return new ResolvedMedia
                    {
                        DirectUrl = muxed.Url,
                        Title = video.Title,
                        Duration = video.Duration,
                        OriginalUrl = url
                    };
                }

                var videoOnly = manifest.GetVideoStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();

                if (videoOnly != null)
                {
                    _logger.LogWarning("Only video-only stream (no audio) for: {Url}", url);
                    return new ResolvedMedia
                    {
                        DirectUrl = videoOnly.Url,
                        Title = video.Title,
                        Duration = video.Duration,
                        OriginalUrl = url
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "YoutubeExplode stream extraction failed, trying OpenGraph fallback for: {Url}", url);
            }

            // Fallback: try OpenGraph meta tags from the YouTube page
            var ogResult = await ResolveViaOpenGraphAsync(url);
            if (ogResult != null && !ogResult.HasError && !string.IsNullOrEmpty(ogResult.DirectUrl))
            {
                // OpenGraph for YouTube returns /embed/ URLs which are HTML pages, not video files.
                // Only use the result if it's an actual media URL.
                if (!ogResult.DirectUrl.Contains("/embed/"))
                {
                    if (!string.IsNullOrEmpty(videoTitle))
                        ogResult.Title = videoTitle;
                    if (videoDuration.HasValue)
                        ogResult.Duration = videoDuration;
                    return ogResult;
                }

                _logger.LogWarning("OpenGraph returned an embed URL (not playable): {EmbedUrl}", ogResult.DirectUrl);
            }

            // Last resort: return the original URL — LibVLC has a built-in YouTube handler
            _logger.LogWarning("Passing URL directly to LibVLC: {Url}", url);
            return new ResolvedMedia
            {
                DirectUrl = url,
                Title = videoTitle ?? "",
                Duration = videoDuration,
                OriginalUrl = url
            };
        }

        private async Task<ResolvedMedia> ResolveViaOpenGraphAsync(string url)
        {
            _logger.LogInformation("🌐 Resolving via OpenGraph: {Url}", url);

            string html;
            try
            {
                html = await _http.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP fetch failed for: {Url}", url);
                return null;
            }

            string videoUrl = MatchFirst(OgVideoRegex, html) ?? MatchFirst(OgVideoRev, html);
            string title = MatchFirst(OgTitleRegex, html) ?? MatchFirst(OgTitleRev, html);

            if (string.IsNullOrEmpty(videoUrl))
            {
                _logger.LogWarning("No og:video found in page: {Url}", url);
                // Return the original URL as fallback — LibVLC may handle it
                return new ResolvedMedia
                {
                    DirectUrl = url,
                    Title = title ?? "",
                    OriginalUrl = url
                };
            }

            videoUrl = WebUtility.HtmlDecode(videoUrl);
            _logger.LogInformation("✅ Resolved og:video = {VideoUrl}", videoUrl);

            return new ResolvedMedia
            {
                DirectUrl = videoUrl,
                Title = title != null ? WebUtility.HtmlDecode(title) : "",
                OriginalUrl = url
            };
        }

        private static bool IsYouTubeUrl(string url)
        {
            return url.Contains("youtube.com") || url.Contains("youtu.be");
        }

        private static string MatchFirst(Regex regex, string input)
        {
            var match = regex.Match(input);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    public class ResolvedMedia
    {
        public string DirectUrl { get; set; }
        public string Title { get; set; }
        public TimeSpan? Duration { get; set; }
        public string OriginalUrl { get; set; }

        /// <summary>User-facing error message. When set, DirectUrl should not be used.</summary>
        public string Error { get; set; }

        public bool HasError => !string.IsNullOrEmpty(Error);

        public static ResolvedMedia WithError(string originalUrl, string errorMessage)
        {
            return new ResolvedMedia
            {
                OriginalUrl = originalUrl,
                Error = errorMessage
            };
        }
    }
}
