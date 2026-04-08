using System;
using Microsoft.Extensions.Logging;
using LibVLCSharp.Shared;

namespace DropCast.Services
{
    /// <summary>
    /// Gère le trimming vidéo à la lecture via les options media de LibVLC.
    /// Aucun exe externe requis (ni ffmpeg) — utilise LibVLC déjà présent dans le projet.
    /// 
    /// Utilisation : appeler <see cref="ApplyTrim"/> sur un objet <see cref="Media"/>
    /// avant de le passer au MediaPlayer.
    /// </summary>
    public class VideoTrimmer
    {
        private readonly ILogger<VideoTrimmer> _logger;

        public VideoTrimmer(ILogger<VideoTrimmer> logger)
        {
            _logger = logger;
        }

        public bool IsAvailable => true;

        /// <summary>
        /// Applies start/stop time options to a LibVLC <see cref="Media"/> for playback trimming.
        /// Call this before passing the media to MediaPlayer.Play().
        /// </summary>
        /// <param name="media">The LibVLC media object.</param>
        /// <param name="startSeconds">Start time in seconds (null = from beginning).</param>
        /// <param name="endSeconds">Stop time in seconds (null = until end).</param>
        public void ApplyTrim(Media media, double? startSeconds, double? endSeconds)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));

            if (startSeconds.HasValue && startSeconds.Value > 0)
            {
                media.AddOption(string.Format(":start-time={0:F2}", startSeconds.Value));
                _logger.LogInformation("✂️ Trim start: {Start}s", startSeconds.Value);
            }

            if (endSeconds.HasValue && endSeconds.Value > 0)
            {
                media.AddOption(string.Format(":stop-time={0:F2}", endSeconds.Value));
                _logger.LogInformation("✂️ Trim end: {End}s", endSeconds.Value);
            }
        }
    }
}
