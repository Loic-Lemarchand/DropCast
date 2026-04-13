using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DropCast.Android.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace DropCast.Android.Services;

/// <summary>
/// Resolves video URLs from platforms to direct playable URLs.
/// Mirrors the desktop DropCast approach:
/// - YouTube: via YoutubeExplode (pure .NET)
/// - Instagram, TikTok: via Cobalt API, Instagram GraphQL, embed scraping, yt-dlp (cascade)
/// - Others: via OpenGraph meta tag extraction
/// </summary>
public class VideoResolver
{
    private readonly ILogger<VideoResolver> _logger;
    private readonly YoutubeClient _youtube;
    private readonly HttpClient _http;
    private readonly HttpClientHandler _httpHandler;

    // yt-dlp binary (downloaded to app internal storage at runtime)
    private string? _ytdlpPath;
    private bool? _ytdlpAvailable;
    private readonly SemaphoreSlim _ytdlpLock = new(1, 1);

    private static readonly Regex OgVideoRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:video(?::url)?[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OgVideoRev = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*property\s*=\s*[""']og:video(?::url)?[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OgTitleRegex = new(
        @"<meta\s[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OgTitleRev = new(
        @"<meta\s[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*property\s*=\s*[""']og:title[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex InstagramShortcodeRegex = new(
        @"instagram\.com/(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VideoUrlInJsonRegex = new(
        @"""video_url""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex ContentUrlJsonRegex = new(
        @"""contentUrl""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex VideoVersionsRegex = new(
        @"""video_versions""\s*:\s*\[\s*\{[^}]*""url""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex LsdTokenRegex = new(
        @"""LSD"",\[\],\{""token"":""([^""]+)"""
        + @"\}",
        RegexOptions.Compiled);

    private static readonly Regex LsdTokenAltRegex = new(
        @"\blsd[""']\s*:\s*[""']([^""']+)[""']",
        RegexOptions.Compiled);

    public VideoResolver(ILogger<VideoResolver> logger)
    {
        _logger = logger;

        _httpHandler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _http = new HttpClient(_httpHandler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

        var ytHandler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var ytHttp = new HttpClient(ytHandler) { Timeout = TimeSpan.FromSeconds(30) };
        ytHttp.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        ytHttp.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _youtube = new YoutubeClient(ytHttp);
    }

    public async Task<ResolvedMedia?> ResolveAsync(string url)
    {
        try
        {
            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
                return await ResolveYouTubeAsync(url);
            if (url.Contains("instagram.com") || url.Contains("tiktok.com"))
                return await ResolveInstagramTikTokAsync(url);
            return await ResolveViaOpenGraphAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve: {Url}", url);
            return ResolvedMedia.WithError(url, "❌ Impossible de lire cette vidéo");
        }
    }

    private async Task<ResolvedMedia> ResolveYouTubeAsync(string url)
    {
        _logger.LogInformation("🎬 Resolving YouTube: {Url}", url);

        // Strategy 1: Cobalt API (fast, ~2-3s response)
        var cobalt = await TryCobaltAsync(url);
        if (cobalt != null)
        {
            cobalt.NeedsDownload = true;
            return cobalt;
        }

        // Strategy 2: YoutubeExplode with direct stream download (handles throttle bypass)
        _logger.LogInformation("🔄 YouTube: Cobalt failed, trying YoutubeExplode...");
        try
        {
            var videoId = YoutubeExplode.Videos.VideoId.Parse(url);
            var videoTask = _youtube.Videos.GetAsync(videoId).AsTask();
            videoTask.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId).AsTask();

            string? videoTitle = null;
            TimeSpan? videoDuration = null;
            if (videoTask.Status == TaskStatus.RanToCompletion)
            {
                videoTitle = videoTask.Result.Title;
                videoDuration = videoTask.Result.Duration;
            }

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
                // Return the CDN URL directly — VideoView streams it via progressive HTTP download.
                // YoutubeExplode's GetManifestAsync already decoded the 'n' throttle parameter,
                // so this URL serves at full speed without needing CopyToAsync.
                _logger.LogInformation("✅ YouTube: {Title} ({Quality}) — streaming CDN URL",
                    videoTitle ?? "?", muxed.VideoQuality);

                return new ResolvedMedia
                {
                    DirectUrl = muxed.Url,
                    Title = videoTitle,
                    Duration = videoDuration,
                    OriginalUrl = url,
                    NeedsDownload = false
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YoutubeExplode failed for: {Url}", url);
        }

        return ResolvedMedia.WithError(url, "❌ Impossible de résoudre YouTube");
    }

    private async Task EnsureYtDlpAsync()
    {
        if (_ytdlpPath != null && File.Exists(_ytdlpPath))
            return;
        await _ytdlpLock.WaitAsync();
        try
        {
            if (_ytdlpPath != null && File.Exists(_ytdlpPath))
                return;
            var dir = Path.Combine(
                global::Android.App.Application.Context.FilesDir!.AbsolutePath, "ytdlp");
            Directory.CreateDirectory(dir);
            var binPath = Path.Combine(dir, "yt-dlp");
            if (File.Exists(binPath))
            {
                new Java.IO.File(binPath).SetExecutable(true, true);
                _ytdlpPath = binPath;
                return;
            }
            string binaryName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "yt-dlp_linux_aarch64",
                Architecture.X64 => "yt-dlp_linux",
                _ => throw new PlatformNotSupportedException(
                    $"yt-dlp not available for {RuntimeInformation.OSArchitecture}")
            };
            _logger.LogInformation("⬇️ Downloading yt-dlp ({Arch})...", RuntimeInformation.OSArchitecture);
            using var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var response = await downloadClient.GetAsync(
                $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{binaryName}",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }
            new Java.IO.File(binPath).SetExecutable(true, true);
            _ytdlpPath = binPath;
            _logger.LogInformation("✅ yt-dlp downloaded ({Size} KB)", new FileInfo(binPath).Length / 1024);
        }
        finally
        {
            _ytdlpLock.Release();
        }
    }

    private async Task<bool> IsYtDlpAvailableAsync()
    {
        if (_ytdlpAvailable.HasValue) return _ytdlpAvailable.Value;
        try
        {
            await EnsureYtDlpAsync();
            var psi = new ProcessStartInfo
            {
                FileName = _ytdlpPath!,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _ytdlpAvailable = false;
                return false;
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = await Task.Run(() => process.WaitForExit(15000));
            _ytdlpAvailable = exited && process.ExitCode == 0;
            if (_ytdlpAvailable.Value)
                _logger.LogInformation("✅ yt-dlp available: {Version}", (await stdoutTask).Trim());
            else
                _logger.LogWarning("❌ yt-dlp not usable: exit={Code} {Err}", process.ExitCode, await stderrTask);
            return _ytdlpAvailable.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ yt-dlp binary not compatible with this device");
            _ytdlpAvailable = false;
            return false;
        }
    }

    private async Task<ResolvedMedia> ResolveInstagramTikTokAsync(string url)
    {
        _logger.LogInformation("🔧 Resolving Instagram/TikTok: {Url}", url);

        // Fire all strategies in parallel — first success wins, 15s global timeout
        var strategies = new List<Task<ResolvedMedia?>> { TryCobaltAsync(url) };

        if (url.Contains("instagram.com"))
        {
            strategies.Add(TryInstagramGraphQLAsync(url));
            strategies.Add(TryInstagramApiAsync(url));
            strategies.Add(TryInstagramEmbedAsync(url));
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(15));

        while (strategies.Count > 0)
        {
            var winner = await Task.WhenAny(strategies.Cast<Task>().Append(timeout));

            if (winner == timeout)
            {
                _logger.LogWarning("⏱️ Instagram/TikTok: global timeout reached (15s)");
                break;
            }

            var strategyTask = (Task<ResolvedMedia?>)winner;
            strategies.Remove(strategyTask);

            try
            {
                var result = await strategyTask;
                if (result is { HasError: false, DirectUrl: not null })
                {
                    result.NeedsDownload = true;
                    if (url.Contains("instagram.com"))
                        result.Referrer ??= "https://www.instagram.com/";
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Strategy failed");
            }
        }

        // TikTok-only fallback: yt-dlp binary
        if (url.Contains("tiktok.com") && await IsYtDlpAvailableAsync())
        {
            try { return await DownloadViaYtDlpAsync(url); }
            catch (Exception ex) { _logger.LogWarning(ex, "yt-dlp failed"); }
        }

        return ResolvedMedia.WithError(url, "❌ Impossible de résoudre le lien");
    }

    private async Task<ResolvedMedia> ResolveJsonViaYtDlpAsync(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ytdlpPath!,
            Arguments = $"--no-playlist --dump-json -f \"best[vcodec=h264]/best[vcodec^=avc]/best[ext=mp4]/best\" \"{url}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start yt-dlp process");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = await Task.Run(() => process.WaitForExit(30000));
        if (!exited)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("yt-dlp timed out");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException($"yt-dlp exit={process.ExitCode}: {stderr}");
        var json = JObject.Parse(stdout);
        var directUrl = (string?)json["url"];
        if (string.IsNullOrEmpty(directUrl))
        {
            var formats = json["formats"] as JArray;
            if (formats is { Count: > 0 })
                directUrl = (string?)formats[formats.Count - 1]["url"];
        }
        if (string.IsNullOrEmpty(directUrl))
            throw new InvalidOperationException("yt-dlp returned no URL");
        var title = (string?)json["title"] ?? "";
        var durationSec = (double?)json["duration"];
        TimeSpan? duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : null;
        var headers = json["http_headers"] as JObject;
        var referrer = (string?)headers?["Referer"];
        var userAgent = (string?)headers?["User-Agent"];
        _logger.LogInformation("✅ yt-dlp resolved: {Title} → {Url}",
            title, directUrl[..Math.Min(80, directUrl.Length)]);
        return new ResolvedMedia
        {
            DirectUrl = directUrl,
            Title = title,
            Duration = duration,
            OriginalUrl = url,
            Referrer = referrer,
            UserAgent = userAgent,
            NeedsDownload = true
        };
    }

    private async Task<ResolvedMedia> DownloadViaYtDlpAsync(string url)
    {
        var tempDir = Path.Combine(
            global::Android.App.Application.Context.CacheDir!.AbsolutePath, "dropcast");
        Directory.CreateDirectory(tempDir);
        CleanOldTempFiles(tempDir);
        var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp4");
        var psi = new ProcessStartInfo
        {
            FileName = _ytdlpPath!,
            Arguments = $"--no-playlist -f \"best[vcodec=h264]/best[vcodec^=avc]/best[ext=mp4]/best\" -o \"{tempFile}\" \"{url}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        _logger.LogInformation("⬇️ yt-dlp downloading to: {Path}", tempFile);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start yt-dlp process");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = await Task.Run(() => process.WaitForExit(60000));
        if (!exited)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("yt-dlp download timed out");
        }
        await stdoutTask;
        var stderr = await stderrTask;
        if (!File.Exists(tempFile))
            throw new InvalidOperationException($"yt-dlp download failed: {stderr}");
        _logger.LogInformation("✅ yt-dlp downloaded: {Size} KB", new FileInfo(tempFile).Length / 1024);
        return new ResolvedMedia
        {
            DirectUrl = tempFile,
            OriginalUrl = url,
            NeedsDownload = false
        };
    }

    private static readonly (string Host, string Path)[] CobaltInstances =
    [
        ("cobalt-api.kwiatekmiki.com", "/"),
        ("cobalt-api.ayo.tf", "/"),
        ("cobalt.tskau.team", "/"),
        ("cobalt-backend.canine.tools", "/"),
        ("cobalt-api.hyper.lol", "/"),
    ];

    private async Task<ResolvedMedia?> TryCobaltAsync(string url)
    {
        // Fire all instances in parallel — first success wins, 6s total timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var tasks = CobaltInstances
            .Select(inst => TrySingleCobaltAsync(inst.Host, inst.Path, url, cts.Token))
            .ToList();

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            try
            {
                var result = await completed;
                if (result != null)
                {
                    await cts.CancelAsync();
                    return result;
                }
            }
            catch { }
        }
        return null;
    }

    private async Task<ResolvedMedia?> TrySingleCobaltAsync(
        string host, string path, string url, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("🔄 Cobalt [{Host}]: {Url}", host, url);
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}{path}");
            var body = new JObject { ["url"] = url, ["videoQuality"] = "480", ["youtubeVideoCodec"] = "h264", ["downloadMode"] = "auto" };
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None));
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = "";
                try { errorBody = await response.Content.ReadAsStringAsync(ct); } catch { }
                _logger.LogWarning("⚠️ Cobalt {Host}: HTTP {Status} — {Body}",
                    host, response.StatusCode, errorBody[..Math.Min(200, errorBody.Length)]);
                return null;
            }
            string json = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Cobalt {Host} response: {Json}", host, json[..Math.Min(300, json.Length)]);
            var data = JObject.Parse(json);
            var result = ParseCobaltResponse(data, url, host);
            if (result == null)
                _logger.LogWarning("⚠️ Cobalt {Host}: unparseable response: {Json}",
                    host, json[..Math.Min(200, json.Length)]);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Cobalt {Host} failed", host);
            return null;
        }
    }

    private ResolvedMedia? ParseCobaltResponse(JObject data, string url, string host)
    {
        string? status = data["status"]?.ToString();
        if (status is "redirect" or "tunnel" or "stream")
        {
            string? videoUrl = data["url"]?.ToString();
            if (!string.IsNullOrEmpty(videoUrl))
            {
                _logger.LogInformation("✅ Cobalt [{Host}]: {Status}", host, status);
                return new ResolvedMedia
                {
                    DirectUrl = videoUrl,
                    OriginalUrl = url,
                    NeedsDownload = true
                };
            }
        }
        else if (status == "picker")
        {
            var picker = data["picker"] as JArray;
            var video = picker?.FirstOrDefault(p => p["type"]?.ToString() == "video");
            string? videoUrl = video?["url"]?.ToString()
                ?? picker?.FirstOrDefault()?["url"]?.ToString();
            if (!string.IsNullOrEmpty(videoUrl))
            {
                _logger.LogInformation("✅ Cobalt [{Host}]: picker", host);
                return new ResolvedMedia
                {
                    DirectUrl = videoUrl,
                    OriginalUrl = url,
                    NeedsDownload = true
                };
            }
        }
        return null;
    }

    private async Task<ResolvedMedia?> TryInstagramEmbedAsync(string url)
    {
        var shortcodeMatch = InstagramShortcodeRegex.Match(url);
        if (!shortcodeMatch.Success)
        {
            _logger.LogWarning("⚠️ Instagram: could not extract shortcode from: {Url}", url);
            return null;
        }

        var shortcode = shortcodeMatch.Groups[1].Value;
        // Try both /p/ and /reel/ embed paths since URL structure may vary
        string[] embedPaths = [$"p/{shortcode}", $"reel/{shortcode}"];

        foreach (var path in embedPaths)
        {
            try
            {
                var embedUrl = $"https://www.instagram.com/{path}/embed/captioned/";
                _logger.LogInformation("🔄 Instagram embed: {Url}", embedUrl);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var request = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                request.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                request.Headers.Add("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                var response = await _http.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("⚠️ Instagram embed {Path}: HTTP {Status}", path, response.StatusCode);
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogDebug("Instagram embed {Path}: {Len} chars", path, html.Length);

                // Look for video_url in embedded JSON data
                var videoUrlMatch = VideoUrlInJsonRegex.Match(html);
                if (videoUrlMatch.Success)
                {
                    var videoUrl = videoUrlMatch.Groups[1].Value
                        .Replace("\\u0026", "&")
                        .Replace("\\/", "/");
                    _logger.LogInformation("✅ Instagram embed [{Path}]: found video_url", path);
                    return new ResolvedMedia
                    {
                        DirectUrl = videoUrl,
                        OriginalUrl = url,
                        Referrer = "https://www.instagram.com/",
                        NeedsDownload = true
                    };
                }

                // Look for contentUrl in JSON-LD
                var contentUrlMatch = ContentUrlJsonRegex.Match(html);
                if (contentUrlMatch.Success)
                {
                    var videoUrl = contentUrlMatch.Groups[1].Value
                        .Replace("\\u0026", "&")
                        .Replace("\\/", "/");
                    _logger.LogInformation("✅ Instagram embed [{Path}]: found contentUrl", path);
                    return new ResolvedMedia
                    {
                        DirectUrl = videoUrl,
                        OriginalUrl = url,
                        Referrer = "https://www.instagram.com/",
                        NeedsDownload = true
                    };
                }

                // Look for video_versions array in script data
                var videoVersionsMatch = VideoVersionsRegex.Match(html);
                if (videoVersionsMatch.Success)
                {
                    var videoUrl = videoVersionsMatch.Groups[1].Value
                        .Replace("\\u0026", "&")
                        .Replace("\\/", "/");
                    _logger.LogInformation("✅ Instagram embed [{Path}]: found video_versions", path);
                    return new ResolvedMedia
                    {
                        DirectUrl = videoUrl,
                        OriginalUrl = url,
                        Referrer = "https://www.instagram.com/",
                        NeedsDownload = true
                    };
                }

                // Fallback: look for og:video in embed page
                string? ogVideo = MatchFirst(OgVideoRegex, html) ?? MatchFirst(OgVideoRev, html);
                if (!string.IsNullOrEmpty(ogVideo))
                {
                    _logger.LogInformation("✅ Instagram embed [{Path}]: found og:video", path);
                    return new ResolvedMedia
                    {
                        DirectUrl = WebUtility.HtmlDecode(ogVideo),
                        OriginalUrl = url,
                        Referrer = "https://www.instagram.com/",
                        NeedsDownload = true
                    };
                }

                _logger.LogWarning("⚠️ Instagram embed {Path}: no video found in HTML", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Instagram embed {Path} failed", path);
            }
        }
        return null;
    }

    private async Task<ResolvedMedia?> TryInstagramApiAsync(string url)
    {
        var shortcodeMatch = InstagramShortcodeRegex.Match(url);
        if (!shortcodeMatch.Success)
            return null;

        var shortcode = shortcodeMatch.Groups[1].Value;
        long mediaId = ShortcodeToMediaId(shortcode);

        try
        {
            _logger.LogInformation("🔄 Instagram API: media/{MediaId}/info/", mediaId);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://i.instagram.com/api/v1/media/{mediaId}/info/");
            request.Headers.Add("User-Agent",
                "Instagram 275.0.0.27.98 Android (33/13; 420dpi; 1080x2400; samsung; SM-S908B; b0q; qcom; en_US; 458229237)");
            request.Headers.Add("X-IG-App-ID", "936619743392459");

            var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("⚠️ Instagram API: HTTP {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var data = JObject.Parse(json);
            var items = data["items"] as JArray;
            if (items is not { Count: > 0 })
                return null;

            var item = items[0];
            var videoVersions = item["video_versions"] as JArray;
            if (videoVersions is not { Count: > 0 })
                return null;

            var videoUrl = (string?)videoVersions[0]["url"];
            if (string.IsNullOrEmpty(videoUrl))
                return null;

            var title = (string?)item["caption"]?["text"] ?? "";
            _logger.LogInformation("✅ Instagram API: resolved video URL");

            return new ResolvedMedia
            {
                DirectUrl = videoUrl,
                OriginalUrl = url,
                Title = title.Length > 100 ? title[..100] : title,
                Referrer = "https://www.instagram.com/",
                NeedsDownload = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Instagram API failed");
            return null;
        }
    }

    private static long ShortcodeToMediaId(string shortcode)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        long id = 0;
        foreach (char c in shortcode)
        {
            int index = alphabet.IndexOf(c);
            if (index < 0) continue;
            id = id * 64 + index;
        }
        return id;
    }

    private async Task<ResolvedMedia?> TryInstagramGraphQLAsync(string url)
    {
        var shortcodeMatch = InstagramShortcodeRegex.Match(url);
        if (!shortcodeMatch.Success)
            return null;

        var shortcode = shortcodeMatch.Groups[1].Value;

        try
        {
            _logger.LogInformation("🔄 Instagram GraphQL: {Shortcode}", shortcode);

            // Step 1: Fetch the Instagram page to get tokens and possibly embedded video data
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var pageReq = new HttpRequestMessage(HttpMethod.Get, url);
            pageReq.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            pageReq.Headers.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            pageReq.Headers.Add("Sec-Fetch-Dest", "document");
            pageReq.Headers.Add("Sec-Fetch-Mode", "navigate");
            pageReq.Headers.Add("Sec-Fetch-Site", "none");

            var pageResp = await _http.SendAsync(pageReq, cts1.Token);
            if (!pageResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("⚠️ Instagram GraphQL: page fetch HTTP {Status}", pageResp.StatusCode);
                return null;
            }

            var html = await pageResp.Content.ReadAsStringAsync(cts1.Token);
            _logger.LogDebug("Instagram page: {Len} chars", html.Length);

            // Try to extract video URL directly from page HTML
            // Instagram embeds post data in <script type=\"application/json\" data-sjs> tags
            var directResult = ExtractVideoFromHtml(html, url);
            if (directResult != null)
            {
                _logger.LogInformation("✅ Instagram page scrape: found video URL directly in HTML");
                return directResult;
            }

            // Extract LSD token for GraphQL query
            string? lsd = MatchFirst(LsdTokenRegex, html) ?? MatchFirst(LsdTokenAltRegex, html);
            if (string.IsNullOrEmpty(lsd))
            {
                _logger.LogWarning("⚠️ Instagram GraphQL: no LSD token in {Len} chars", html.Length);
                return null;
            }

            // Extract CSRF token from cookie container
            string? csrfToken = null;
            try
            {
                var igCookies = _httpHandler.CookieContainer.GetCookies(
                    new Uri("https://www.instagram.com/"));
                csrfToken = igCookies["csrftoken"]?.Value;
            }
            catch { /* best-effort */ }

            // Step 2: GraphQL query for the post data
            var variables = new JObject { ["shortcode"] = shortcode };
            var formData = new List<KeyValuePair<string, string>>
            {
                new("lsd", lsd),
                new("variables", variables.ToString(Newtonsoft.Json.Formatting.None)),
                new("doc_id", "8845758582119845")
            };

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var gqlReq = new HttpRequestMessage(HttpMethod.Post,
                "https://www.instagram.com/graphql/query/");
            gqlReq.Content = new FormUrlEncodedContent(formData);
            gqlReq.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            gqlReq.Headers.Add("X-FB-LSD", lsd);
            gqlReq.Headers.Add("X-IG-App-ID", "936619743392459");
            gqlReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
            gqlReq.Headers.Add("Referer", url);
            gqlReq.Headers.Add("Origin", "https://www.instagram.com");
            if (!string.IsNullOrEmpty(csrfToken))
                gqlReq.Headers.Add("X-CSRFToken", csrfToken);

            var gqlResp = await _http.SendAsync(gqlReq, cts2.Token);
            if (!gqlResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("⚠️ Instagram GraphQL query: HTTP {Status}", gqlResp.StatusCode);
                return null;
            }

            var json = await gqlResp.Content.ReadAsStringAsync(cts2.Token);
            return ParseInstagramGraphQLResponse(json, url);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⚠️ Instagram GraphQL: timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Instagram GraphQL failed");
            return null;
        }
    }

    private ResolvedMedia? ExtractVideoFromHtml(string html, string originalUrl)
    {
        var videoUrlMatch = VideoUrlInJsonRegex.Match(html);
        if (videoUrlMatch.Success)
        {
            var videoUrl = DecodeInstagramUrl(videoUrlMatch.Groups[1].Value);
            if (videoUrl.StartsWith("http"))
                return new ResolvedMedia
                {
                    DirectUrl = videoUrl,
                    OriginalUrl = originalUrl,
                    Referrer = "https://www.instagram.com/",
                    NeedsDownload = true
                };
        }

        var videoVersionsMatch = VideoVersionsRegex.Match(html);
        if (videoVersionsMatch.Success)
        {
            var videoUrl = DecodeInstagramUrl(videoVersionsMatch.Groups[1].Value);
            if (videoUrl.StartsWith("http"))
                return new ResolvedMedia
                {
                    DirectUrl = videoUrl,
                    OriginalUrl = originalUrl,
                    Referrer = "https://www.instagram.com/",
                    NeedsDownload = true
                };
        }

        var contentUrlMatch = ContentUrlJsonRegex.Match(html);
        if (contentUrlMatch.Success)
        {
            var videoUrl = DecodeInstagramUrl(contentUrlMatch.Groups[1].Value);
            if (videoUrl.StartsWith("http"))
                return new ResolvedMedia
                {
                    DirectUrl = videoUrl,
                    OriginalUrl = originalUrl,
                    Referrer = "https://www.instagram.com/",
                    NeedsDownload = true
                };
        }

        return null;
    }

    private ResolvedMedia? ParseInstagramGraphQLResponse(string json, string originalUrl)
    {
        try
        {
            var data = JObject.Parse(json);
            string? videoUrl = null;
            string? title = null;

            // Structure 1: data.xdt_shortcode_media
            var media = data.SelectToken("data.xdt_shortcode_media");
            if (media != null)
            {
                videoUrl = (string?)media["video_url"];
                title = (string?)media.SelectToken("edge_media_to_caption.edges[0].node.text");
            }

            // Structure 2: data.shortcode_media (older response format)
            if (string.IsNullOrEmpty(videoUrl))
            {
                media = data.SelectToken("data.shortcode_media");
                if (media != null)
                {
                    videoUrl = (string?)media["video_url"];
                    title = (string?)media.SelectToken("edge_media_to_caption.edges[0].node.text");
                }
            }

            // Structure 3: xdt_api__v1__media__shortcode__web_info (newest format)
            if (string.IsNullOrEmpty(videoUrl))
            {
                var items = data.SelectToken(
                    "data.xdt_api__v1__media__shortcode__web_info.items") as JArray;
                if (items is { Count: > 0 })
                {
                    var versions = items[0]["video_versions"] as JArray;
                    if (versions is { Count: > 0 })
                        videoUrl = (string?)versions[0]["url"];
                    title ??= (string?)items[0].SelectToken("caption.text");
                }
            }

            if (string.IsNullOrEmpty(videoUrl))
            {
                _logger.LogWarning("⚠️ Instagram GraphQL: no video URL in response ({Len} chars)",
                    json.Length);
                return null;
            }

            videoUrl = DecodeInstagramUrl(videoUrl);
            _logger.LogInformation("✅ Instagram GraphQL query: resolved video URL");

            return new ResolvedMedia
            {
                DirectUrl = videoUrl,
                OriginalUrl = originalUrl,
                Title = (title?.Length > 100 ? title[..100] : title) ?? "",
                Referrer = "https://www.instagram.com/",
                NeedsDownload = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Instagram GraphQL: failed to parse response");
            return null;
        }
    }

    private static string DecodeInstagramUrl(string url)
    {
        return url
            .Replace("\\u0026", "&")
            .Replace("\\u003C", "<")
            .Replace("\\u003E", ">")
            .Replace("\\/", "/")
            .Replace("\\\\", "\\");
    }

    private async Task<ResolvedMedia> ResolveViaOpenGraphAsync(string url)
    {
        _logger.LogInformation("🌐 OpenGraph: {Url}", url);
        string html;
        try { html = await _http.GetStringAsync(url); }
        catch { return ResolvedMedia.WithError(url, "❌ Impossible de charger la page"); }
        string? videoUrl = MatchFirst(OgVideoRegex, html) ?? MatchFirst(OgVideoRev, html);
        string? title = MatchFirst(OgTitleRegex, html) ?? MatchFirst(OgTitleRev, html);
        if (string.IsNullOrEmpty(videoUrl))
            return ResolvedMedia.WithError(url, "❌ Aucune vidéo trouvée");
        videoUrl = WebUtility.HtmlDecode(videoUrl);
        return new ResolvedMedia
        {
            DirectUrl = videoUrl,
            Title = title != null ? WebUtility.HtmlDecode(title) : "",
            OriginalUrl = url
        };
    }

    public async Task<string?> DownloadToTempAsync(string url, string? referrer = null, string? userAgent = null)
    {
        try
        {
            string tempDir = Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "dropcast");
            Directory.CreateDirectory(tempDir);
            CleanOldTempFiles(tempDir);
            string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp4");
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referrer))
                request.Headers.Referrer = new Uri(referrer);
            if (!string.IsNullOrEmpty(userAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = File.Create(tempFile);
            await stream.CopyToAsync(fs);
            _logger.LogInformation("📥 Downloaded {Size}KB → {Path}",
                new FileInfo(tempFile).Length / 1024, Path.GetFileName(tempFile));
            return tempFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download video from: {Url}", url);
            return null;
        }
    }

    private static void CleanOldTempFiles(string dir)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir, "*.mp4"))
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddHours(-1))
                    File.Delete(file);
            }
        }
        catch { }
    }

    private static string? MatchFirst(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }
}
