using System.Text.RegularExpressions;

namespace DropCast.Android.Services;

public static class TrimParser
{
    private static readonly Regex TrimRegex = new(
        @"\[\s*(\d+(?::\d+)*)?\s*-\s*(\d+(?::\d+)*)?\s*\]",
        RegexOptions.Compiled);

    public static bool TryParse(string? text, out double? startSeconds, out double? endSeconds)
    {
        startSeconds = null;
        endSeconds = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = TrimRegex.Match(text);
        if (!match.Success) return false;

        string startPart = match.Groups[1].Value;
        string endPart = match.Groups[2].Value;

        if (string.IsNullOrEmpty(startPart) && string.IsNullOrEmpty(endPart))
            return false;

        if (!string.IsNullOrEmpty(startPart))
            startSeconds = ParseTimeToSeconds(startPart);
        if (!string.IsNullOrEmpty(endPart))
            endSeconds = ParseTimeToSeconds(endPart);

        if (startSeconds.HasValue && endSeconds.HasValue && startSeconds.Value >= endSeconds.Value)
            return false;

        return true;
    }

    public static string StripTrim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        return TrimRegex.Replace(text, "").Trim();
    }

    private static double ParseTimeToSeconds(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length == 1)
        {
            double.TryParse(parts[0], out double sec);
            return sec;
        }
        if (parts.Length == 2)
        {
            int.TryParse(parts[0], out int min);
            int.TryParse(parts[1], out int sec);
            return min * 60.0 + sec;
        }
        if (parts.Length == 3)
        {
            int.TryParse(parts[0], out int h);
            int.TryParse(parts[1], out int min);
            int.TryParse(parts[2], out int sec);
            return h * 3600.0 + min * 60.0 + sec;
        }
        return 0;
    }
}
