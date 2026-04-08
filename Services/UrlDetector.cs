using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DropCast.Services
{
    /// <summary>
    /// Detects video/media URLs from popular platforms in plain text.
    /// Supports YouTube, TikTok, Instagram Reels, Twitter/X, and direct media links.
    /// </summary>
    public static class UrlDetector
    {
        private static readonly Regex YoutubeRegex = new Regex(
            @"https?://(?:www\.)?(?:youtube\.com/(?:watch\?[^\s]*v=|shorts/|embed/)|youtu\.be/)[a-zA-Z0-9_-]+[^\s]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TikTokRegex = new Regex(
            @"https?://(?:www\.)?(?:tiktok\.com/@[\w.-]+/video/\d+|vm\.tiktok\.com/\w+)[^\s]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex InstagramRegex = new Regex(
            @"https?://(?:www\.)?instagram\.com/(?:reel|p|reels)/[\w-]+[^\s]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TwitterRegex = new Regex(
            @"https?://(?:www\.)?(?:twitter\.com|x\.com)/\w+/status/\d+[^\s]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RedditRegex = new Regex(
            @"https?://(?:www\.)?reddit\.com/r/\w+/comments/\w+[^\s]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DirectMediaRegex = new Regex(
            @"https?://\S+\.(?:mp4|mov|avi|mkv|webm|mp3|wav|ogg|flac|jpg|jpeg|png|gif)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex[] AllPatterns = new Regex[]
        {
            YoutubeRegex, TikTokRegex, InstagramRegex, TwitterRegex, RedditRegex, DirectMediaRegex
        };

        private static readonly string[] PlatformNames = new string[]
        {
            "YouTube", "TikTok", "Instagram", "Twitter", "Reddit", "Direct"
        };

        public static List<DetectedUrl> DetectUrls(string text)
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

        /// <summary>
        /// Strips detected URLs from text, leaving only the caption/comment.
        /// </summary>
        public static string StripUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string result = text;
            foreach (var pattern in AllPatterns)
            {
                result = pattern.Replace(result, "");
            }

            return result.Trim();
        }
    }

    public class DetectedUrl
    {
        public string OriginalUrl { get; set; }
        public string Platform { get; set; }
    }
}
