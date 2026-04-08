using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace DropCast.Services
{
    /// <summary>
    /// Resolves video URLs from platforms to direct playable URLs.
    /// - YouTube : via YoutubeExplode (pure .NET)
    /// - TikTok, Instagram, Twitter/X, Reddit : via OpenGraph meta tag extraction (HTTP)
    /// Utilise yt-dlp (téléchargé automatiquement dans AppData) pour Instagram/TikTok.
    /// </summary>
    public class VideoResolver
    {
        private readonly ILogger<VideoResolver> _logger;
        private readonly YoutubeClient _youtube;
        private readonly HttpClient _http;
        private string _ytdlpPath;

        // Cache des résolutions (TTL 1h — les URLs CDN YouTube expirent après ~6h)
        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        private class CacheEntry
        {
            public ResolvedMedia Media;
            public DateTime FetchedAtUtc;
        }

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
        /// Resolves any supported platform URL to a direct playable video URL.
        /// Returns null only on unrecoverable errors. Check <see cref="ResolvedMedia.Error"/> for user-facing messages.
        /// </summary>
        public async Task<ResolvedMedia> ResolveAsync(string url)
        {
            // Cache hit?
            if (_cache.TryGetValue(url, out var entry) && DateTime.UtcNow - entry.FetchedAtUtc < CacheTtl)
            {
                _logger.LogInformation("⚡ Cache hit: {Url}", url);
                return entry.Media;
            }

            try
            {
                ResolvedMedia result;
                if (IsYouTubeUrl(url))
                    result = await ResolveYouTubeAsync(url);
                else if (IsInstagramUrl(url) || IsTikTokUrl(url))
                    result = await ResolveViaYtDlpAsync(url);
                else
                    result = await ResolveViaOpenGraphAsync(url);

                // Cacher les résultats réussis avec un stream direct
                if (result != null && !result.HasError && result.IsDirectStream)
                    _cache[url] = new CacheEntry { Media = result, FetchedAtUtc = DateTime.UtcNow };

                return result;
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

            try
            {
                var videoId = YoutubeExplode.Videos.VideoId.Parse(url);

                // Lancer metadata en arrière-plan (non-bloquant pour le titre)
                var videoTask = _youtube.Videos.GetAsync(videoId).AsTask();
                videoTask.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

                // Seul le manifest est nécessaire pour la lecture
                var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId).AsTask();

                // Récupérer le titre si déjà disponible (non-bloquant)
                if (videoTask.Status == TaskStatus.RanToCompletion)
                {
                    videoTitle = videoTask.Result.Title;
                    videoDuration = videoTask.Result.Duration;
                }

                // Préférer MP4 (H.264) ≤480p — démarrage VLC beaucoup plus rapide
                var muxed = manifest.GetMuxedStreams()
                    .Where(s => s.VideoQuality.MaxHeight <= 480 && s.Container.Name == "mp4")
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault()
                    ?? manifest.GetMuxedStreams()
                        .Where(s => s.VideoQuality.MaxHeight <= 480)
                        .OrderByDescending(s => s.VideoQuality.MaxHeight)
                        .FirstOrDefault()
                    ?? manifest.GetMuxedStreams()
                        .OrderBy(s => s.VideoQuality.MaxHeight)
                        .FirstOrDefault();

                if (muxed != null)
                {
                    _logger.LogInformation("✅ YouTube resolved: {Title} ({Quality})", videoTitle ?? "?", muxed.VideoQuality);
                    return new ResolvedMedia
                    {
                        DirectUrl = muxed.Url,
                        Title = videoTitle,
                        Duration = videoDuration,
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
                        Title = videoTitle,
                        Duration = videoDuration,
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

        private static bool IsInstagramUrl(string url)
        {
            return url.Contains("instagram.com/");
        }

        private static bool IsTikTokUrl(string url)
        {
            return url.Contains("tiktok.com/") || url.Contains("vm.tiktok.com/");
        }

        /// <summary>
        /// Télécharge yt-dlp.exe dans %LOCALAPPDATA%\DropCast\ si absent.
        /// </summary>
        private async Task EnsureYtDlpAsync()
        {
            if (_ytdlpPath != null && File.Exists(_ytdlpPath))
                return;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropCast");
            Directory.CreateDirectory(dir);
            var exePath = Path.Combine(dir, "yt-dlp.exe");

            if (File.Exists(exePath))
            {
                _ytdlpPath = exePath;
                return;
            }

            _logger.LogInformation("⬇️ Downloading yt-dlp to {Path}...", exePath);
            var response = await _http.GetAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using (var fs = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            _ytdlpPath = exePath;
            _logger.LogInformation("✅ yt-dlp downloaded successfully.");
        }

        /// <summary>
        /// Résout une URL via yt-dlp --dump-json. Retourne le flux direct ou fallback OpenGraph/VLC.
        /// </summary>
        private async Task<ResolvedMedia> ResolveViaYtDlpAsync(string url)
        {
            _logger.LogInformation("🔧 Resolving via yt-dlp: {Url}", url);

            try
            {
                await EnsureYtDlpAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download yt-dlp, falling back to OpenGraph");
                return await ResolveViaOpenGraphAsync(url);
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _ytdlpPath,
                    Arguments = $"--no-playlist --dump-json -f \"best[ext=mp4]/best\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                var exited = await Task.Run(() => process.WaitForExit(30000));
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    _logger.LogWarning("yt-dlp timed out for: {Url}", url);
                    return await ResolveViaOpenGraphAsync(url);
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                {
                    _logger.LogWarning("yt-dlp failed (exit={Code}): {Err}", process.ExitCode, stderr);
                    return await ResolveViaOpenGraphAsync(url);
                }

                var json = JObject.Parse(stdout);
                var directUrl = (string)json["url"];

                // Si url est null, chercher dans formats
                if (string.IsNullOrEmpty(directUrl))
                {
                    var formats = json["formats"] as JArray;
                    if (formats != null && formats.Count > 0)
                    {
                        // Prendre le dernier format (meilleure qualité)
                        directUrl = (string)formats[formats.Count - 1]["url"];
                    }
                }

                if (string.IsNullOrEmpty(directUrl))
                {
                    _logger.LogWarning("yt-dlp returned no URL for: {Url}", url);
                    return await ResolveViaOpenGraphAsync(url);
                }

                var title = (string)json["title"] ?? "";
                var durationSec = (double?)json["duration"];
                TimeSpan? duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : (TimeSpan?)null;

                _logger.LogInformation("✅ yt-dlp resolved: {Title} → {DirectUrl}", title, directUrl.Substring(0, Math.Min(80, directUrl.Length)));

                return new ResolvedMedia
                {
                    DirectUrl = directUrl,
                    Title = title,
                    Duration = duration,
                    OriginalUrl = url
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "yt-dlp resolution failed for: {Url}", url);
                return await ResolveViaOpenGraphAsync(url);
            }
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

        /// <summary>True si DirectUrl est un flux direct (CDN). False si c'est une URL de plateforme que VLC gère en interne.</summary>
        public bool IsDirectStream { get; set; } = true;

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
