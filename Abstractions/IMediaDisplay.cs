using System;

namespace DropCast.Abstractions
{
    /// <summary>
    /// Abstraction over the UI that displays memes/media.
    /// Desktop (WinForms overlay), Android (floating overlay), web, etc.
    /// </summary>
    public interface IMediaDisplay
    {
        event EventHandler DisplayCompleted;
        void ShowText(string message);
        void ShowImage(string imageUrl, string caption);
        void ShowVideo(string videoUrl, string caption, double? trimStartSeconds, double? trimEndSeconds, string referrer = null, string userAgent = null);
        void PlayAudio(string audioUrl, string caption);
    }
}
