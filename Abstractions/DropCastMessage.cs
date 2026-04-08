namespace DropCast.Abstractions
{
    /// <summary>
    /// Platform-agnostic message received from any source (Discord, WhatsApp, API, etc.).
    /// </summary>
    public class DropCastMessage
    {
        public string Text { get; set; }
        public string Caption { get; set; }
        public string AuthorName { get; set; }
        public string SourcePlatform { get; set; }
        public string AuthorAvatarUrl { get; set; }
        public MediaContent[] Attachments { get; set; }
    }
}
