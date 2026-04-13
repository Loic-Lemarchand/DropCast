using System.Text.RegularExpressions;
using DropCast.Android.Models;

namespace DropCast.Android.Services;

public static class UrlDetector
{
    private static readonly Regex YoutubeRegex = new(
        @"https?://(?:www\.)?(?:youtube\.com/(?:watch\?[^\s]*v=|shorts/|embed/)|youtu\.be/)[a-zA-Z0-9_-]+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TikTokRegex = new(
        @"https?://(?:www\.)?(?:tiktok\.com/@[\w.-]+/video/\d+|vm\.tiktok\.com/\w+)[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InstagramRegex = new(
        @"https?://(?:www\.)?instagram\.com/(?:reel|p|reels)/[\w-]+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TwitterRegex = new(
        @"https?://(?:www\.)?(?:twitter\.com|x\.com)/\w+/status/\d+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RedditRegex = new(
        @"https?://(?:www\.)?reddit\.com/r/\w+/comments/\w+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DirectMediaRegex = new(
        @"https?://\S+\.(?:mp4|mov|avi|mkv|webm|mp3|wav|ogg|flac|jpg|jpeg|png|gif)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] AllPatterns =
    [
        YoutubeRegex, TikTokRegex, InstagramRegex, TwitterRegex, RedditRegex, DirectMediaRegex
    ];

    private static readonly string[] PlatformNames =
    [
        "YouTube", "TikTok", "Instagram", "Twitter", "Reddit", "Direct"
    ];

    public static List<DetectedUrl> DetectUrls(string? text)
    {
        var results = new List<DetectedUrl>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        for (int i = 0; i < AllPatterns.Length; i++)
        {
            foreach (Match match in AllPatterns[i].Matches(text))
            {
                results.Add(new DetectedUrl
                {
                    OriginalUrl = match.Value.TrimEnd(')', ']', '>'),
                    Platform = PlatformNames[i]
                });
            }
        }
        return results;
    }

    public static string StripUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        string result = text;
        foreach (var pattern in AllPatterns)
            result = pattern.Replace(result, "");
        return result.Trim();
    }
}
