using DropCast.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DropCast.Sources
{
    /// <summary>
    /// Message source that receives local file drops via <see cref="DropOverlayForm"/>.
    /// When a file is dropped, a <see cref="DropCaptionDialog"/> is shown for caption/trim,
    /// then a <see cref="DropCastMessage"/> is fired through the pipeline.
    /// The file is also uploaded to the currently selected Discord channel so all
    /// listeners can see it.
    /// </summary>
    public class LocalDropMessageSource : IMessageSource
    {
        private DropOverlayForm _overlay;
        private readonly DiscordMessageSource _discordSource;

        private static readonly Dictionary<string, MediaType> ExtensionToMediaType =
            new Dictionary<string, MediaType>(StringComparer.OrdinalIgnoreCase)
        {
            // Video
            { ".mp4", MediaType.Video }, { ".m4v", MediaType.Video }, { ".mov", MediaType.Video },
            { ".avi", MediaType.Video }, { ".mkv", MediaType.Video }, { ".webm", MediaType.Video },
            { ".wmv", MediaType.Video }, { ".flv", MediaType.Video }, { ".mpeg", MediaType.Video },
            { ".mpg", MediaType.Video }, { ".3gp", MediaType.Video }, { ".ts", MediaType.Video },
            { ".ogv", MediaType.Video },
            // Audio
            { ".mp3", MediaType.Audio }, { ".wav", MediaType.Audio }, { ".ogg", MediaType.Audio },
            { ".flac", MediaType.Audio }, { ".aac", MediaType.Audio }, { ".wma", MediaType.Audio },
            { ".m4a", MediaType.Audio }, { ".opus", MediaType.Audio },
            // Image
            { ".jpg", MediaType.Image }, { ".jpeg", MediaType.Image }, { ".png", MediaType.Image },
            { ".gif", MediaType.Image }, { ".bmp", MediaType.Image }, { ".webp", MediaType.Image },
            { ".tiff", MediaType.Image }, { ".tif", MediaType.Image }, { ".svg", MediaType.Image },
            { ".heic", MediaType.Image }, { ".avif", MediaType.Image },
        };

        public string PlatformName { get { return "Local Drop"; } }

        public event EventHandler<DropCastMessage> MessageReceived;

        public LocalDropMessageSource(DiscordMessageSource discordSource)
        {
            _discordSource = discordSource;
        }

        public Task ConnectAsync()
        {
            _overlay = new DropOverlayForm();
            _overlay.FilesDropped += OnFilesDropped;
            _overlay.Show();
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            if (_overlay != null)
            {
                _overlay.FilesDropped -= OnFilesDropped;
                _overlay.Close();
                _overlay.Dispose();
                _overlay = null;
            }
            return Task.CompletedTask;
        }

        private void OnFilesDropped(object sender, string[] files)
        {
            string filePath = files[0];

            using (var dialog = new DropCaptionDialog(filePath))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                string ext = Path.GetExtension(filePath);
                MediaType mediaType;
                if (string.IsNullOrEmpty(ext) || !ExtensionToMediaType.TryGetValue(ext, out mediaType))
                    mediaType = MediaType.Image;

                string fileUri = new Uri(filePath).AbsoluteUri;

                // Embed trim syntax in the text so TrimParser picks it up in the pipeline
                string trimText = "";
                if (dialog.TrimStart.HasValue || dialog.TrimEnd.HasValue)
                {
                    trimText = string.Format(" [{0}-{1}]",
                        dialog.TrimStart.HasValue ? dialog.TrimStart.Value.ToString("F0") : "",
                        dialog.TrimEnd.HasValue ? dialog.TrimEnd.Value.ToString("F0") : "");
                }

                var msg = new DropCastMessage
                {
                    Text = (dialog.Caption ?? "") + trimText,
                    Caption = dialog.Caption ?? "",
                    AuthorName = Environment.UserName,
                    SourcePlatform = PlatformName,
                    Attachments = new[]
                    {
                        new MediaContent
                        {
                            Type = mediaType,
                            Url = fileUri,
                            FileName = Path.GetFileName(filePath),
                            LocalPath = filePath
                        }
                    }
                };

                MessageReceived?.Invoke(this, msg);

                // Upload to the current Discord channel so all listeners see the meme
                if (_discordSource != null)
                {
                    string uploadPath = filePath;
                    string uploadCaption = dialog.Caption ?? "";
                    _ = Task.Run(async () =>
                    {
                        try { await _discordSource.UploadFileAsync(uploadPath, uploadCaption); }
                        catch { /* logged inside UploadFileAsync */ }
                    });
                }
            }
        }
    }
}
