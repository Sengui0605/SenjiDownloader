using System.Text.Json;
using System.Text.RegularExpressions;

namespace SenjiDownloader;

public enum JobState { Queued, Downloading, Processing, Paused, Completed, Failed, Cancelled }
public enum JobKind { Download, Convert }
public sealed record MediaJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobKind Kind { get; init; }
    public string Source { get; init; } = "";
    public string Title { get; init; } = "Preparando video…";
    public string Folder { get; init; } = "";
    public string Quality { get; init; } = "1080p";
    public string Format { get; init; } = "MP4";
    public JobState State { get; init; } = JobState.Queued;
    public double Progress { get; init; }
    public string Detail { get; init; } = "En cola";
    public string OutputFile { get; init; } = "";
    public string Error { get; init; } = "";
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public bool IsActive => State is JobState.Downloading or JobState.Processing;
    public bool IsTerminal => State is JobState.Completed or JobState.Failed or JobState.Cancelled;
}

public sealed class Preferences
{
    public string DownloadFolder { get; set; } = AppPaths.DefaultDownloads;
    public int ParallelDownloads { get; set; } = 3;
    public string Quality { get; set; } = "1080p";
    public string Format { get; set; } = "MP4";
    public bool StartWithWindows { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;
    public string ReleaseRepository { get; set; } = "Sengui0605/SenjiDownloader";
    public string IntegratedPath { get; set; } = "";
    public DateTime LastEngineUpdate { get; set; }
    public string EngineVersion { get; set; } = "";
    public string DenoVersion { get; set; } = "";
    public string CookieBrowser { get; set; } = "Ninguno";
}

public static class AppPaths
{
    public static string Base { get; } = AppContext.BaseDirectory;
    public static string Data { get; private set; } = "";
    public static string Tools => Path.Combine(Base, "tools");
    public static string DefaultDownloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SenjiDownloader");
    public static void Initialize(string? overridePath = null)
    {
        Data = overridePath ?? Path.Combine(Base, "data");
        try
        {
            Directory.CreateDirectory(Data);
            var probe = Path.Combine(Data, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe);
        }
        catch when (overridePath == null)
        {
            Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SenjiDownloader");
            Directory.CreateDirectory(Data);
        }
    }
    public static string Tool(string name) => Path.Combine(Tools, name + ".exe");
    public static void Log(string text)
    {
        try
        {
            var file = Path.Combine(Data, "app.log");
            lock (LogLock)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 2_000_000) File.Move(file, file + ".old", true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
        }
        catch { }
    }
    private static readonly object LogLock = new();
}

public static class Storage
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();
    public static T Load<T>(string name, Func<T> fallback)
    {
        var path = Path.Combine(AppPaths.Data, name);
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback(); }
        catch (Exception ex)
        {
            if (File.Exists(path)) AppPaths.Log($"No se pudo leer {name}: {ex.Message}. Se conserva el archivo.");
            return fallback();
        }
    }
    public static void Save<T>(string name, T value)
    {
        lock (Gate)
        {
            var path = Path.Combine(AppPaths.Data, name);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, true);
        }
    }
}

public static partial class LinkParser
{
    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
    public static string[] Parse(string text)
    {
        return Links().Matches(text).Select(m => m.Value.TrimEnd(',', ';', ')', ']'))
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) && uri.UserInfo.Length == 0)
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
