namespace DropCast.Abstractions
{
    public class MediaContent
    {
        public MediaType Type { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string LocalPath { get; set; }

        /// <summary>Trim start in seconds (optional, for video cutting).</summary>
        public double? StartTime { get; set; }

        /// <summary>Trim end in seconds (optional, for video cutting).</summary>
        public double? EndTime { get; set; }
    }
}
