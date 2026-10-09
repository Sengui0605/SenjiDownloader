using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace SenjiDownloader;

public sealed class UpdateService : IDisposable
{
    private readonly Preferences settings;
    private readonly QueueEngine engine;
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(8) };
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string Status { get; private set; } = "Las actualizaciones se comprueban en segundo plano.";
    public string? PendingFile { get; private set; }
    public string? PendingHash { get; private set; }
    public event Action? Changed;
    public UpdateService(Preferences settings, QueueEngine engine)
    {
        this.settings = settings; this.engine = engine;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SenjiDownloader/" + CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    public async Task Check(bool force = false)
    {
        if (!await checkGate.WaitAsync(0)) return;
        try
        {
            if (!force && !settings.AutoUpdate) return;
            SetStatus("Comprobando versiones…");
            var token = lifetime.Token;
            var repo = settings.ReleaseRepository.Trim();
            if (repo.Length > 0)
            {
                if (!Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("El canal debe tener el formato usuario/repositorio.");
                using var response = await client.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", token);
                if (response.IsSuccessStatusCode)
                {
                    using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
                    if (Version.TryParse(tag.TrimStart('v'), out var remote) && remote > Version.Parse(CurrentVersion))
                    {
                        var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == "SenjiDownloader.exe");
                        if (asset.ValueKind != JsonValueKind.Undefined)
                        {
                            var url = asset.GetProperty("browser_download_url").GetString()!;
                            EnsureGitHubUrl(url);
                            var hash = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
                            if (hash?.StartsWith("sha256:") == true) hash = hash[7..];
                            else
                            {
                                var checksum = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == "SenjiDownloader.exe.sha256");
                                if (checksum.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("La versión publicada no tiene checksum SHA-256.");
                                var checksumUrl = checksum.GetProperty("browser_download_url").GetString()!; EnsureGitHubUrl(checksumUrl);
                                hash = (await client.GetStringAsync(checksumUrl, token)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                            }
                            ValidateHash(hash);
                            var directory = Path.Combine(AppPaths.Data, "updates"); Directory.CreateDirectory(directory);
                            var file = Path.Combine(directory, "SenjiDownloader-" + remote + ".exe");
                            SetStatus($"Descargando SenjiDownloader {remote} en segundo plano…");
                            await DownloadVerified(url, file, hash!, token);
                            PendingFile = file; PendingHash = hash;
                        }
                    }
                }
                else if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            }
            if (force || DateTime.UtcNow - settings.LastEngineUpdate > TimeSpan.FromHours(24))
                await UpdateEngines(token);
            SetStatus(PendingFile != null ? "Nueva versión lista. Se aplicará cuando termine la cola." : $"SenjiDownloader {CurrentVersion} · al día. Motor: {settings.EngineVersion}.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetStatus("No se pudo comprobar la actualización. Se reintentará más tarde.");
            AppPaths.Log("Actualizaciones: " + ex.Message);
        }
        finally { checkGate.Release(); }
    }
    private async Task UpdateEngines(CancellationToken token)
    {
        if (!engine.BeginMaintenance())
        {
            SetStatus("La actualización del motor espera a que terminen las descargas."); return;
        }
        try
        {
            using var release = await client.GetFromJsonAsync<JsonDocument>("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", token);
            if (release == null) return;
            var tag = release.RootElement.GetProperty("tag_name").GetString()!;
            if (tag != settings.EngineVersion)
            {
                var assets = release.RootElement.GetProperty("assets").EnumerateArray().ToArray();
                var asset = assets.First(a => a.GetProperty("name").GetString() == "yt-dlp.exe");
                var url = asset.GetProperty("browser_download_url").GetString()!;
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                string hash;
                if (digest?.StartsWith("sha256:") == true) hash = digest[7..];
                else
                {
                    var checksums = assets.First(a => a.GetProperty("name").GetString() == "SHA2-256SUMS");
                    var text = await client.GetStringAsync(checksums.GetProperty("browser_download_url").GetString()!, token);
                    hash = text.Split('\n').Select(line => line.Trim()).First(line => Regex.IsMatch(line, @"\s\*?yt-dlp\.exe$")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                }
                SetStatus("Actualizando el motor de descarga…");
                var temporary = Path.Combine(AppPaths.Tools, "yt-dlp.update.exe");
                await DownloadVerified(url, temporary, hash, token);
                var reported = await ProcessRunner.Run(temporary, ["--version"], null, token);
                if (!reported.Trim().Contains(tag, StringComparison.Ordinal)) throw new InvalidDataException("El motor descargado no coincide con la versión publicada.");
                File.Move(temporary, AppPaths.Tool("yt-dlp"), true);
                settings.EngineVersion = tag;
            }
            using var denoRelease = await client.GetFromJsonAsync<JsonDocument>("https://api.github.com/repos/denoland/deno/releases/latest", token);
            if (denoRelease != null)
            {
                var denoTag = denoRelease.RootElement.GetProperty("tag_name").GetString()!;
                if (denoTag != settings.DenoVersion)
                {
                    var asset = denoRelease.RootElement.GetProperty("assets").EnumerateArray().First(a => a.GetProperty("name").GetString() == "deno-x86_64-pc-windows-msvc.zip");
                    var digest = asset.GetProperty("digest").GetString();
                    if (digest?.StartsWith("sha256:") != true) throw new InvalidDataException("La publicación de Deno no tiene checksum SHA-256.");
                    SetStatus("Actualizando el componente de JavaScript…");
                    var zip = Path.Combine(AppPaths.Tools, "deno.update.zip");
                    await DownloadVerified(asset.GetProperty("browser_download_url").GetString()!, zip, digest[7..], token);
                    var temporary = Path.Combine(AppPaths.Tools, "deno.update.exe");
                    using (var archive = ZipFile.OpenRead(zip))
                    {
                        var entry = archive.GetEntry("deno.exe") ?? throw new InvalidDataException("El archivo de Deno no contiene el ejecutable.");
                        entry.ExtractToFile(temporary, true);
                    }
                    var reported = await ProcessRunner.Run(temporary, ["--version"], null, token);
                    if (!reported.Contains(denoTag.TrimStart('v'), StringComparison.Ordinal)) throw new InvalidDataException("La versión de Deno no coincide.");
                    File.Move(temporary, AppPaths.Tool("deno"), true); File.Delete(zip); settings.DenoVersion = denoTag;
                }
            }
            settings.LastEngineUpdate = DateTime.UtcNow;
            Storage.Save("preferences.json", settings);
        }
        finally { engine.EndMaintenance(); }
    }
    private async Task DownloadVerified(string url, string path, string hash, CancellationToken token)
    {
        EnsureGitHubUrl(url); ValidateHash(hash);
        var temporary = path + ".download";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var stream = File.Create(temporary)) await response.Content.CopyToAsync(stream, token);
            await using var input = File.OpenRead(temporary);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El checksum de la actualización no coincide.");
            input.Close(); File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
    internal static void ValidateHash(string? hash)
    {
        if (hash == null || !Regex.IsMatch(hash, "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("Checksum SHA-256 inválido.");
    }
    private static void EnsureGitHubUrl(string text)
    {
        var url = new Uri(text);
        if (url.Scheme != "https" || url.Host != "github.com") throw new InvalidDataException("La actualización debe provenir de GitHub por HTTPS.");
    }
    public bool TryApply()
    {
        if (PendingFile == null || PendingHash == null || !engine.BeginMaintenance()) return false;
        try
        {
            var directory = Path.Combine(AppPaths.Data, "updates", "helper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var helper = Path.Combine(directory, "SenjiUpdater.exe"); File.Copy(Environment.ProcessPath!, helper);
            var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--update-helper", Environment.ProcessId.ToString(), PendingFile, Environment.ProcessPath!, PendingHash, AppPaths.Data, Program.NoIntegration ? "--no-integration" : "normal" }) info.ArgumentList.Add(arg);
            _ = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar el actualizador.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo aplicar la actualización. Puedes reintentarlo."); AppPaths.Log(ex.ToString()); engine.EndMaintenance(); return false;
        }
    }
    public static int RunHelper(string[] args)
    {
        string? backup = null;
        Process? updatedProcess = null;
        try
        {
            if (args.Length is not (6 or 7)) return 2;
            AppPaths.Initialize(args[5]); ValidateHash(args[4]);
            var pid = int.Parse(args[1]);
            var source = Path.GetFullPath(args[2]); var target = Path.GetFullPath(args[3]);
            if (Path.GetFileName(target) != "SenjiDownloader.exe") throw new InvalidDataException("Destino de actualización inválido.");
            using (var input = File.OpenRead(source))
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(args[4], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La actualización cambió después de su verificación.");
            try { using var parent = Process.GetProcessById(pid); if (!parent.WaitForExit(60_000)) throw new TimeoutException("La aplicación sigue en ejecución."); } catch (ArgumentException) { }
            backup = target + ".previous";
            File.Copy(target, backup, true);
            var newFile = target + ".new"; File.Copy(source, newFile, true); File.Move(newFile, target, true);
            using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, SingleInstance.UpdateReady);
            signal.Reset();
            var start = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--background"); if (Program.NoIntegration) start.ArgumentList.Add("--no-integration"); updatedProcess = Process.Start(start);
            if (!signal.WaitOne(TimeSpan.FromSeconds(35)))
            {
                if (updatedProcess != null && !updatedProcess.HasExited) { updatedProcess.Kill(entireProcessTree: true); updatedProcess.WaitForExit(5000); }
                throw new InvalidOperationException("La nueva versión no inició. Se restaurará la anterior.");
            }
            AppPaths.Log("Actualización aplicada y arranque verificado."); return 0;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Fallo al aplicar actualización: " + ex);
            if (backup != null && File.Exists(backup))
            {
                try
                {
                    File.Copy(backup, args[3], true);
                    var restored = new ProcessStartInfo(args[3]) { UseShellExecute = false, CreateNoWindow = true };
                    restored.ArgumentList.Add("--background"); if (Program.NoIntegration) restored.ArgumentList.Add("--no-integration"); Process.Start(restored);
                }
                catch (Exception rollback) { AppPaths.Log("Restauración: " + rollback); }
            }
            return 1;
        }
    }
    public void Dispose() { lifetime.Cancel(); client.Dispose(); }
}
