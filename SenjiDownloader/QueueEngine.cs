using System.Globalization;
using System.Text.Json;

namespace SenjiDownloader;

public sealed class QueueEngine : IDisposable
{
    private readonly object gate = new();
    private readonly List<MediaJob> jobs;
    private readonly Dictionary<Guid, CancellationTokenSource> running = new();
    private readonly Preferences settings;
    private readonly Action<Action> dispatch;
    private bool disposed;
    private bool maintenance;
    private bool queuePaused;
    public event Action? Changed;
    public event Action<MediaJob>? Finished;
    public bool IsPaused { get { lock (gate) return queuePaused; } }
    public bool IsBusy { get { lock (gate) return running.Count > 0; } }
    public MediaJob[] Snapshot { get { lock (gate) return jobs.ToArray(); } }
    public QueueEngine(Preferences settings, Action<Action> dispatch, bool load = true)
    {
        this.settings = settings; this.dispatch = dispatch;
        jobs = load ? Storage.Load("queue.json", () => new List<MediaJob>()) : new();
        for (var i = 0; i < jobs.Count; i++)
            if (jobs[i].IsActive || jobs[i].State == JobState.Queued)
                jobs[i] = jobs[i] with { State = JobState.Paused, Detail = "Listo para continuar", Progress = 0 };
    }
    public int AddDownloads(string text, string folder, string quality, string format)
    {
        var links = LinkParser.Parse(text);
        if (links.Length == 0) throw new ArgumentException("Pega al menos un enlace http o https válido.");
        folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder);
        int added = 0;
        lock (gate)
        {
            foreach (var url in links)
            {
                if (jobs.Any(j => j.Source == url && !j.IsTerminal && j.Quality == quality && j.Format == format)) continue;
                jobs.Add(new MediaJob { Source = url, Folder = folder, Quality = quality, Format = format, Title = new Uri(url).Host }); added++;
            }
        }
        Persist(); Changed?.Invoke(); Pump(); return added;
    }
    public void AddConversions(IEnumerable<string> files, string folder, string format)
    {
        folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder);
        lock (gate)
        {
            foreach (var file in files.Where(File.Exists))
                jobs.Add(new MediaJob { Kind = JobKind.Convert, Source = Path.GetFullPath(file), Title = Path.GetFileName(file), Folder = folder, Format = format });
        }
        Persist(); Changed?.Invoke(); Pump();
    }
    public void TogglePause()
    {
        CancellationTokenSource[] tokens;
        lock (gate)
        {
            queuePaused = !queuePaused;
            tokens = queuePaused ? running.Values.ToArray() : [];
            if (queuePaused)
            {
                for (var i = 0; i < jobs.Count; i++)
                    if (jobs[i].IsActive || jobs[i].State == JobState.Queued) jobs[i] = jobs[i] with { State = JobState.Paused, Detail = "Pausado · conserva el avance" };
            }
            else
            {
                for (var i = 0; i < jobs.Count; i++)
                    if (jobs[i].State == JobState.Paused) jobs[i] = jobs[i] with { State = JobState.Queued, Detail = "En cola" };
            }
        }
        foreach (var token in tokens) token.Cancel();
        Persist(); Changed?.Invoke(); Pump();
    }
    public void Pause(Guid id)
    {
        CancellationTokenSource? token;
        lock (gate)
        {
            var index = jobs.FindIndex(j => j.Id == id); if (index < 0) return;
            if (jobs[index].State is JobState.Completed or JobState.Cancelled) return;
            jobs[index] = jobs[index] with { State = JobState.Paused, Detail = "Pausado · conserva el avance" };
            running.TryGetValue(id, out token);
        }
        token?.Cancel(); Persist(); Changed?.Invoke();
    }
    public void Cancel(Guid id)
    {
        CancellationTokenSource? token;
        lock (gate)
        {
            var index = jobs.FindIndex(j => j.Id == id); if (index < 0) return;
            jobs[index] = jobs[index] with { State = JobState.Cancelled, Detail = "Cancelado" };
            running.TryGetValue(id, out token);
        }
        token?.Cancel(); Persist(); Changed?.Invoke();
    }
    public void Retry(Guid id)
    {
        lock (gate)
        {
            var index = jobs.FindIndex(j => j.Id == id); if (index < 0 || jobs[index].IsActive) return;
            jobs[index] = jobs[index] with { State = JobState.Queued, Progress = 0, Detail = "En cola", Error = "" };
            queuePaused = false;
        }
        Persist(); Changed?.Invoke(); Pump();
    }
    public void ClearFinished()
    {
        lock (gate) jobs.RemoveAll(j => j.IsTerminal);
        Persist(); Changed?.Invoke();
    }
    public bool BeginMaintenance()
    {
        lock (gate) { if (running.Count > 0 || maintenance) return false; maintenance = true; return true; }
    }
    public void EndMaintenance() { lock (gate) maintenance = false; Pump(); }
    public void Pump()
    {
        List<(MediaJob job, CancellationTokenSource token)> launch = new();
        lock (gate)
        {
            if (disposed || maintenance || queuePaused) return;
            foreach (var pending in jobs.Where(j => j.State == JobState.Queued).ToArray())
            {
                if (running.Count >= Math.Clamp(settings.ParallelDownloads, 1, 6)) break;
                // Avoid duplicate workers when pause/resume races with process termination.
                if (running.ContainsKey(pending.Id)) continue;
                var token = new CancellationTokenSource(); running[pending.Id] = token;
                var started = pending with { State = JobState.Downloading, Detail = pending.Kind == JobKind.Convert ? "Preparando conversión…" : "Conectando…", Error = "" };
                jobs[jobs.FindIndex(j => j.Id == pending.Id)] = started; launch.Add((started, token));
            }
        }
        foreach (var item in launch) _ = Task.Run(() => Work(item.job, item.token));
        if (launch.Count > 0) Changed?.Invoke();
    }
    private void Update(Guid id, Func<MediaJob, MediaJob> change)
    {
        lock (gate)
        {
            var i = jobs.FindIndex(j => j.Id == id);
            if (i < 0 || jobs[i].State is JobState.Cancelled or JobState.Paused) return;
            if (running.TryGetValue(id, out var active) && active.IsCancellationRequested) return;
            jobs[i] = change(jobs[i]);
        }
        Changed?.Invoke();
    }
    private async Task Work(MediaJob job, CancellationTokenSource cancellation)
    {
        try
        {
            if (job.Kind == JobKind.Download) await Download(job, cancellation.Token);
            else await Convert(job, cancellation.Token);
            Update(job.Id, j => j with { State = JobState.Completed, Progress = 100, Detail = "Completado" });
            var done = Snapshot.FirstOrDefault(j => j.Id == job.Id);
            if (done?.State == JobState.Completed) dispatch(() => Finished?.Invoke(done));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Update(job.Id, j => j with { State = JobState.Failed, Detail = "No se pudo completar · ver detalles", Error = ex.Message });
            AppPaths.Log($"Trabajo {job.Id}: {ex.Message}");
        }
        finally
        {
            lock (gate) running.Remove(job.Id);
            cancellation.Dispose();
            Persist(); Changed?.Invoke(); Pump();
        }
    }
    internal static List<string> DownloadArguments(MediaJob job, string cookieBrowser)
    {
        var args = new List<string>
        {
            "--ignore-config", "--no-playlist", "--newline", "--progress", "--no-colors", "--windows-filenames",
            "--continue", "--no-overwrites", "--retries", "3", "--fragment-retries", "3", "--socket-timeout", "25",
            "--concurrent-fragments", "4", "--ffmpeg-location", AppPaths.Tools,
            "--js-runtimes", "deno:" + AppPaths.Tool("deno"), "--progress-delta", "0.35",
            "--progress-template", "download:SDPROGRESS|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--print", "before_dl:SDTITLE|%(title)s", "--print", "after_move:SDFILE|%(filepath)s", "--no-simulate",
            "--trim-filenames", "180", "-o", Path.Combine(job.Folder, "%(title).160B [%(id)s].%(ext)s")
        };
        if (cookieBrowser is "Chrome" or "Edge" or "Firefox") args.AddRange(["--cookies-from-browser", cookieBrowser.ToLowerInvariant()]);
        if (job.Format == "MP3") args.AddRange(["-f", "bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", "0"]);
        else
        {
            var height = job.Quality switch { "2160p" => "2160", "1440p" => "1440", "1080p" => "1080", "720p" => "720", "480p" => "480", _ => "" };
            var bound = height.Length == 0 ? "" : $"[height<=?{height}]";
            var format = job.Format == "MP4"
                ? $"bv*{bound}[ext=mp4]+ba[ext=m4a]/b{bound}[ext=mp4]/bv*{bound}+ba/b{bound}"
                : $"bv*{bound}+ba/b{bound}";
            args.AddRange(["-f", format, "--merge-output-format", job.Format.ToLowerInvariant()]);
            if (job.Format == "MP4") args.AddRange(["--remux-video", "mp4"]);
        }
        args.Add("--"); args.Add(job.Source); return args;
    }
    private async Task Download(MediaJob job, CancellationToken token)
    {
        await ProcessRunner.Run(AppPaths.Tool("yt-dlp"), DownloadArguments(job, settings.CookieBrowser), line =>
        {
            if (line.StartsWith("SDTITLE|")) Update(job.Id, j => j with { Title = line[8..] });
            else if (line.StartsWith("SDFILE|")) Update(job.Id, j => j with { OutputFile = line[7..] });
            else if (line.StartsWith("SDPROGRESS|"))
            {
                var parts = line.Split('|');
                if (parts.Length >= 4 && double.TryParse(parts[1].Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var progress))
                    Update(job.Id, j => j with { Progress = Math.Clamp(progress, 0, 99.5), State = JobState.Downloading, Detail = $"{parts[2].Trim()}  ·  {parts[3].Trim()} restantes" });
            }
            else if (line.Contains("[Merger]") || line.Contains("[ExtractAudio]") || line.Contains("[VideoRemuxer]"))
                Update(job.Id, j => j with { State = JobState.Processing, Detail = "Procesando archivo…" });
        }, token);
        var finished = Snapshot.FirstOrDefault(j => j.Id == job.Id);
        if (finished != null && finished.State is not (JobState.Paused or JobState.Cancelled) && (finished.OutputFile.Length == 0 || !File.Exists(finished.OutputFile)))
            throw new InvalidOperationException("El motor terminó sin producir un archivo. Consulta los detalles e inténtalo de nuevo.");
    }
    private async Task Convert(MediaJob job, CancellationToken token)
    {
        var durationText = await ProcessRunner.Run(AppPaths.Tool("ffprobe"), ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", job.Source], null, token);
        double.TryParse(durationText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
        var stem = Path.GetFileNameWithoutExtension(job.Source);
        var output = Path.Combine(job.Folder, $"{stem} - convertido-{job.Id.ToString("N")[..6]}.{job.Format.ToLowerInvariant()}");
        // Separate temporary file: never overwrite the source or expose an unfinished conversion as complete.
        var temporary = output + ".partial." + job.Format.ToLowerInvariant();
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", job.Source };
        args.AddRange(job.Format switch
        {
            "MP3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "WAV" => ["-vn", "-c:a", "pcm_s16le", "-ar", "44100"],
            _ => new[] { "-c:v", "libx264", "-preset", "fast", "-crf", "23", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart" }
        });
        args.AddRange(["-progress", "pipe:1", "-nostats", temporary]);
        Update(job.Id, j => j with { State = JobState.Processing, Detail = "Convirtiendo…" });
        try
        {
            await ProcessRunner.Run(AppPaths.Tool("ffmpeg"), args, line =>
            {
                if (line.StartsWith("out_time_us=") && duration > 0 && long.TryParse(line[12..], out var micros))
                    Update(job.Id, j => j with { Progress = Math.Clamp(micros / 1_000_000d / duration * 100, 0, 99.5), Detail = "Convirtiendo…" });
            }, token);
            token.ThrowIfCancellationRequested(); File.Move(temporary, output, false);
            Update(job.Id, j => j with { OutputFile = output });
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
    public void Persist()
    {
        try { Storage.Save("queue.json", Snapshot); }
        catch (Exception ex) { AppPaths.Log("No se pudo guardar la cola: " + ex.Message); }
    }
    public void Dispose()
    {
        CancellationTokenSource[] tokens;
        lock (gate)
        {
            disposed = true; tokens = running.Values.ToArray();
            for (var i = 0; i < jobs.Count; i++) if (jobs[i].IsActive) jobs[i] = jobs[i] with { State = JobState.Paused, Detail = "Listo para continuar" };
        }
        foreach (var token in tokens) token.Cancel(); Persist();
    }
}
