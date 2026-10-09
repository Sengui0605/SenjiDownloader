using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SenjiDownloader;

internal static class SelfTests
{
    public static async Task<int> Run(string report)
    {
        ProcessRunner.TraceProcesses = true;
        var results = new List<object>();
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "integration-fixtures"); Directory.CreateDirectory(root);
        void Pass(string test, object? evidence = null) => results.Add(new { test, passed = true, evidence });
        try
        {
            var source = Path.Combine(root, "source.mp4");
            await ProcessRunner.Run(AppPaths.Tool("ffmpeg"), ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=660", "-t", "8", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", source], null, CancellationToken.None);
            var original = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var downloads = Path.Combine(root, "downloads"); Directory.CreateDirectory(downloads);
            await using var server = new MediaServer(await File.ReadAllBytesAsync(source));
            using var engine = new QueueEngine(new Preferences { ParallelDownloads = 3 }, a => a(), false);
            var links = string.Join('\n', new[] { "a", "b", "c" }.Select(n => server.Url + n + ".mp4"));
            var count = engine.AddDownloads(links, downloads, "1080p", "MP4");
            if (count != 3) throw new Exception("No se encolaron tres enlaces.");
            await Until(() => engine.Snapshot.Any(j => j.Progress > 2 && j.IsActive) || engine.Snapshot.Any(j => j.State == JobState.Failed), 60);
            var startupFailure = engine.Snapshot.FirstOrDefault(j => j.State == JobState.Failed); if (startupFailure != null) throw new Exception(startupFailure.Error);
            var paused = engine.Snapshot.First(j => j.IsActive && j.Progress > 2);
            engine.Pause(paused.Id); await Task.Delay(800);
            if (engine.Snapshot.First(j => j.Id == paused.Id).State != JobState.Paused) throw new Exception("No se pausó el trabajo.");
            engine.Retry(paused.Id);
            await Until(() => engine.Snapshot.Count(j => j.State == JobState.Completed) == 3 || engine.Snapshot.Any(j => j.State == JobState.Failed), 90);
            var failed = engine.Snapshot.FirstOrDefault(j => j.State == JobState.Failed); if (failed != null) throw new Exception(failed.Error);
            if (server.PeakTransfers < 2) throw new Exception("Las transferencias HTTP no se ejecutaron en paralelo.");
            foreach (var job in engine.Snapshot)
                if (!SHA256.HashData(await File.ReadAllBytesAsync(job.OutputFile)).SequenceEqual(original)) throw new Exception("El archivo descargado está incompleto.");
            Pass("Tres descargas reales en paralelo", new { server.PeakTransfers, files = engine.Snapshot.Select(j => Path.GetFileName(j.OutputFile)) });
            Pass("Pausa y reanudación de una transferencia", new { id = paused.Id, rangeRequests = server.RangeRequests });
            foreach (var format in new[] { "MP3", "WAV", "MP4" }) engine.AddConversions([source], downloads, format);
            await Until(() => engine.Snapshot.Count(j => j.State == JobState.Completed) == 6 || engine.Snapshot.Any(j => j.State == JobState.Failed), 60);
            failed = engine.Snapshot.FirstOrDefault(j => j.State == JobState.Failed); if (failed != null) throw new Exception(failed.Error);
            foreach (var job in engine.Snapshot.Where(j => j.Kind == JobKind.Convert))
            {
                var duration = await ProcessRunner.Run(AppPaths.Tool("ffprobe"), ["-v", "error", "-show_entries", "format=duration", "-of", "json", job.OutputFile], null, CancellationToken.None);
                if (double.Parse(JsonDocument.Parse(duration).RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) < 7.9) throw new Exception("Conversión incompleta.");
                Pass("Conversión real a " + job.Format, new { bytes = new FileInfo(job.OutputFile).Length });
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(source)).SequenceEqual(original)) throw new Exception("Se modificó el archivo de origen."); Pass("Original conservado después de tres conversiones");
            engine.AddDownloads(server.Url + "cancel.mp4", downloads, "1080p", "MP4");
            await Until(() => engine.Snapshot.Any(j => j.Source.EndsWith("cancel.mp4") && j.Progress > 1), 45);
            var cancel = engine.Snapshot.First(j => j.Source.EndsWith("cancel.mp4")); engine.Cancel(cancel.Id);
            await Until(() => !engine.IsBusy, 12);
            if (engine.Snapshot.First(j => j.Id == cancel.Id).State != JobState.Cancelled) throw new Exception("El trabajo cancelado cambió de estado."); Pass("Cancelación real sin proceso huérfano");
            engine.AddDownloads(server.Url + "missing.mp4", downloads, "1080p", "MP4");
            await Until(() => engine.Snapshot.Any(j => j.Source.EndsWith("missing.mp4") && j.State == JobState.Failed), 45);
            if (engine.Snapshot.Last().Error.Length == 0) throw new Exception("El error no tiene detalles."); Pass("Error HTTP mostrado y persistido");
            engine.Persist();
            using var restored = new QueueEngine(new Preferences(), a => a());
            if (restored.Snapshot.Length != engine.Snapshot.Length || restored.Snapshot.Count(j => j.State == JobState.Completed) != 6) throw new Exception("No se restauró el historial."); Pass("Persistencia del historial y las rutas de salida");
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, version = UpdateService.CurrentVersion, results }, Storage.Json)); return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = ex.ToString(), results }, Storage.Json)); return 1;
        }
    }
    private static async Task Until(Func<bool> condition, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("La prueba excedió su tiempo máximo."); await Task.Delay(120); }
    }
    private sealed class MediaServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly byte[] content;
        private readonly Task serving;
        private int active, peak, ranges;
        public string Url { get; }
        public int PeakTransfers => peak;
        public int RangeRequests => ranges;
        public MediaServer(byte[] content)
        {
            this.content = content; listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"; serving = Serve();
        }
        private async Task Serve()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Respond(client); } }
            catch (OperationCanceledException) { }
        }
        private async Task Respond(TcpClient client)
        {
            bool transferring = false;
            using (client)
            {
                try
                {
                    using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var request = await reader.ReadLineAsync(stop.Token) ?? ""; var range = 0;
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } line)
                        if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) { int.TryParse(line[13..].Split('-')[0], out range); Interlocked.Increment(ref ranges); }
                    if (request.Contains("missing"))
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token); return;
                    }
                    range = Math.Clamp(range, 0, content.Length - 1);
                    var header = $"HTTP/1.1 {(range > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Type: video/mp4\r\nContent-Length: {content.Length - range}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n";
                    if (range > 0) header += $"Content-Range: bytes {range}-{content.Length - 1}/{content.Length}\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "\r\n"), stop.Token);
                    if (request.StartsWith("HEAD")) return;
                    transferring = true; var concurrent = Interlocked.Increment(ref active);
                    int seen; do { seen = peak; if (seen >= concurrent) break; } while (Interlocked.CompareExchange(ref peak, concurrent, seen) != seen);
                    for (var offset = range; offset < content.Length; offset += 16384)
                    {
                        await stream.WriteAsync(content.AsMemory(offset, Math.Min(16384, content.Length - offset)), stop.Token); await Task.Delay(15, stop.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
                finally { if (transferring) Interlocked.Decrement(ref active); }
            }
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await serving; stop.Dispose(); }
    }
}
