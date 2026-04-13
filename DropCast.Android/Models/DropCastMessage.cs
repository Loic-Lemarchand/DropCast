namespace DropCast.Android.Models;

public class DropCastMessage
{
    public string Text { get; set; } = "";
    public string Caption { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string SourcePlatform { get; set; } = "";
    public string? AuthorAvatarUrl { get; set; }
    public MediaContent[] Attachments { get; set; } = [];
}
