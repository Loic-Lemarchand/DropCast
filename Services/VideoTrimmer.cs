using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LibVLCSharp.Shared;

namespace DropCast.Services
{
    /// <summary>
    /// Gère le trimming vidéo via LibVLC sout (stream output).
    /// Un second MediaPlayer headless télécharge et découpe le segment vidéo
    /// dans un fichier local, puis la lecture se fait depuis ce fichier.
    /// Aucun exécutable externe requis — utilise le LibVLC déjà présent dans le projet.
    /// </summary>
    public class VideoTrimmer
    {
        private readonly ILogger<VideoTrimmer> _logger;
        private readonly LibVLC _libVLC;
        private string _lastTempFile;

        public VideoTrimmer(ILogger<VideoTrimmer> logger)
        {
            _logger = logger;

            try
            {
                Core.Initialize();
                _libVLC = new LibVLC("--no-video-title-show");
                _logger.LogInformation("✅ VideoTrimmer initialisé (LibVLC sout)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ LibVLC init failed for VideoTrimmer");
            }

            CleanupOldTempFiles();
        }

        /// <summary>True si LibVLC sout est disponible pour le pré-découpage.</summary>
        public bool IsAvailable => _libVLC != null;

        /// <summary>
        /// Pré-découpe une vidéo distante via LibVLC sout.
        /// Télécharge uniquement le segment [start-end] et le sauvegarde dans un fichier local.
        /// Retourne le chemin du fichier local, ou null en cas d'échec.
        /// </summary>
        public async Task<string> TrimToFileAsync(string videoUrl, double? startSeconds, double? endSeconds, CancellationToken cancellationToken)
        {
            if (_libVLC == null) return null;
            if (!startSeconds.HasValue && !endSeconds.HasValue) return null;

            CleanupLastTempFile();

            string tempFile = Path.Combine(Path.GetTempPath(), "dropcast_" + Guid.NewGuid().ToString("N") + ".ts");

            _logger.LogInformation("✂️ Trim sout [{Start}-{End}]",
                startSeconds.HasValue ? startSeconds.Value.ToString("F1") : "0",
                endSeconds.HasValue ? endSeconds.Value.ToString("F1") : "fin");

            var tcs = new TaskCompletionSource<bool>();
            MediaPlayer dumpPlayer = null;

            try
            {
                using (var media = new Media(_libVLC, new Uri(videoUrl)))
                {
                    media.AddOption(":network-caching=100");

                    if (startSeconds.HasValue && startSeconds.Value > 0)
                        media.AddOption(string.Format(":start-time={0:F2}", startSeconds.Value));
                    if (endSeconds.HasValue && endSeconds.Value > 0)
                        media.AddOption(string.Format(":stop-time={0:F2}", endSeconds.Value));

                    // sout : copie directe des flux vers fichier local (pas de ré-encodage)
                    string dst = tempFile.Replace("\\", "/");
                    media.AddOption(string.Format(":sout=#standard{{access=file,mux=ts,dst='{0}'}}", dst));
                    media.AddOption(":sout-all");
                    media.AddOption(":sout-keep");

                    dumpPlayer = new MediaPlayer(_libVLC);

                    EventHandler<EventArgs> onEnded = null;
                    EventHandler<EventArgs> onError = null;

                    onEnded = (s, e) =>
                    {
                        dumpPlayer.EndReached -= onEnded;
                        dumpPlayer.EncounteredError -= onError;
                        tcs.TrySetResult(true);
                    };

                    onError = (s, e) =>
                    {
                        dumpPlayer.EndReached -= onEnded;
                        dumpPlayer.EncounteredError -= onError;
                        tcs.TrySetResult(false);
                    };

                    dumpPlayer.EndReached += onEnded;
                    dumpPlayer.EncounteredError += onError;

                    using (cancellationToken.Register(() =>
                    {
                        ThreadPool.QueueUserWorkItem(_ => { try { dumpPlayer.Stop(); } catch { } });
                        tcs.TrySetCanceled();
                    }))
                    {
                        dumpPlayer.Play(media);

                        // Timeout de sécurité : 10s max pour le trim
                        var timeout = Task.Delay(10000);
                        var completed = await Task.WhenAny(tcs.Task, timeout);

                        bool success = false;
                        if (completed == tcs.Task && tcs.Task.Status == TaskStatus.RanToCompletion)
                        {
                            success = tcs.Task.Result;
                        }
                        else if (completed == timeout)
                        {
                            _logger.LogWarning("⏱️ Trim timeout (30s)");
                            ThreadPool.QueueUserWorkItem(_ => { try { dumpPlayer.Stop(); } catch { } });
                            await Task.Delay(500);
                        }

                        // Dispose depuis un thread non-VLC (obligation LibVLC)
                        var p = dumpPlayer;
                        dumpPlayer = null;
                        ThreadPool.QueueUserWorkItem(_ => p.Dispose());

                        if (success && File.Exists(tempFile) && new FileInfo(tempFile).Length > 0)
                        {
                            _lastTempFile = tempFile;
                            _logger.LogInformation("✅ Trim OK : {Size:N0} octets", new FileInfo(tempFile).Length);
                            return tempFile;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (dumpPlayer != null)
                    ThreadPool.QueueUserWorkItem(_ => dumpPlayer.Dispose());
                TryDeleteFile(tempFile);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur trim sout");
                if (dumpPlayer != null)
                    ThreadPool.QueueUserWorkItem(_ => dumpPlayer.Dispose());
            }

            TryDeleteFile(tempFile);
            return null;
        }

        /// <summary>
        /// Fallback : applique les options start/stop time de LibVLC à la lecture.
        /// Utilisé quand le pré-découpage sout échoue.
        /// </summary>
        public void ApplyTrim(Media media, double? startSeconds, double? endSeconds)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));

            if (startSeconds.HasValue && startSeconds.Value > 0)
            {
                media.AddOption(string.Format(":start-time={0:F2}", startSeconds.Value));
                _logger.LogInformation("✂️ VLC trim start: {Start}s", startSeconds.Value);
            }

            if (endSeconds.HasValue && endSeconds.Value > 0)
            {
                media.AddOption(string.Format(":stop-time={0:F2}", endSeconds.Value));
                _logger.LogInformation("✂️ VLC trim end: {End}s", endSeconds.Value);
            }
        }

        /// <summary>Supprime le dernier fichier temporaire créé.</summary>
        public void CleanupLastTempFile()
        {
            if (_lastTempFile != null)
            {
                TryDeleteFile(_lastTempFile);
                _lastTempFile = null;
            }
        }

        private void CleanupOldTempFiles()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "dropcast_*.ts"))
                    TryDeleteFile(f);
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "dropcast_*.mp4"))
                    TryDeleteFile(f);
            }
            catch { }
        }

        private void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
