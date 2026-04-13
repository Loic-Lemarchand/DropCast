namespace DropCast.Android.Models;

public class DetectedUrl
{
    public string OriginalUrl { get; set; } = "";
    public string Platform { get; set; } = "";
}

public class ResolvedMedia
{
    public string DirectUrl { get; set; } = "";
    public string? Title { get; set; }
    public TimeSpan? Duration { get; set; }
    public string OriginalUrl { get; set; } = "";
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public bool NeedsDownload { get; set; }
    public string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public static ResolvedMedia WithError(string originalUrl, string errorMessage) => new()
    {
        OriginalUrl = originalUrl,
        Error = errorMessage
    };
}
