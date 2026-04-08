using System;
using System.Text.RegularExpressions;

namespace DropCast.Services
{
    /// <summary>
    /// Parses trim/cut syntax from message text.
    /// 
    /// Supported formats:
    ///   [30-75]         → seconds
    ///   [0:30-1:15]     → mm:ss
    ///   [1:00:30-1:01]  → hh:mm:ss (mix ok)
    ///   [30-]           → start at 30s, play to end
    ///   [-75]           → start from beginning, stop at 75s
    /// </summary>
    public static class TrimParser
    {
        private static readonly Regex TrimRegex = new Regex(
            @"\[\s*(\d+(?::\d+)*)?\s*-\s*(\d+(?::\d+)*)?\s*\]",
            RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse a trim range from the text.
        /// Returns true if a [start-end] pattern was found.
        /// </summary>
        public static bool TryParse(string text, out double? startSeconds, out double? endSeconds)
        {
            startSeconds = null;
            endSeconds = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = TrimRegex.Match(text);
            if (!match.Success)
                return false;

            string startPart = match.Groups[1].Value;
            string endPart = match.Groups[2].Value;

            if (string.IsNullOrEmpty(startPart) && string.IsNullOrEmpty(endPart))
                return false;

            if (!string.IsNullOrEmpty(startPart))
                startSeconds = ParseTimeToSeconds(startPart);

            if (!string.IsNullOrEmpty(endPart))
                endSeconds = ParseTimeToSeconds(endPart);

            // Validate: start must be before end when both are set
            if (startSeconds.HasValue && endSeconds.HasValue && startSeconds.Value >= endSeconds.Value)
                return false;

            return true;
        }

        /// <summary>
        /// Strips the [start-end] trim syntax from the text.
        /// </summary>
        public static string StripTrim(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return TrimRegex.Replace(text, "").Trim();
        }

        /// <summary>
        /// Parses "75", "1:15", or "1:02:30" into total seconds.
        /// </summary>
        private static double ParseTimeToSeconds(string value)
        {
            string[] parts = value.Split(':');

            if (parts.Length == 1)
            {
                // Pure seconds: "75"
                double sec;
                double.TryParse(parts[0], out sec);
                return sec;
            }

            if (parts.Length == 2)
            {
                // mm:ss: "1:15"
                int min, sec;
                int.TryParse(parts[0], out min);
                int.TryParse(parts[1], out sec);
                return min * 60.0 + sec;
            }

            if (parts.Length == 3)
            {
                // hh:mm:ss: "1:02:30"
                int h, min, sec;
                int.TryParse(parts[0], out h);
                int.TryParse(parts[1], out min);
                int.TryParse(parts[2], out sec);
                return h * 3600.0 + min * 60.0 + sec;
            }

            return 0;
        }
    }
}
