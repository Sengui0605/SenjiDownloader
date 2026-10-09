using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace SenjiDownloader;

internal static class ToolBootstrap
{
    private static readonly string[] required = ["yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe"];
    private static readonly Lazy<Task> preparation = new(() => Task.Run(Prepare));
    public static Task EnsureAsync() => preparation.Value;

    private static void Prepare()
    {
        var watch = Stopwatch.StartNew();
        var marker = Path.Combine(AppPaths.Tools, "bundled-version.txt");
        if (required.All(name => File.Exists(Path.Combine(AppPaths.Tools, name))) &&
            File.Exists(marker) && File.ReadAllText(marker).Trim() == UpdateService.CurrentVersion) return;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SenjiDownloader.tools.zip");
        if (resource == null)
        {
            if (required.All(name => File.Exists(Path.Combine(AppPaths.Tools, name)))) return;
            throw new FileNotFoundException("Esta compilación no incluye los componentes. Descarga la versión oficial completa.");
        }
        Directory.CreateDirectory(AppPaths.Tools);
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var root = Path.GetFullPath(AppPaths.Tools).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta de componente inválida.");
            // Keep newer independently updated engines when installing a new app version.
            if (File.Exists(target) && entry.FullName is "yt-dlp.exe" or "deno.exe" or "versions.json") continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".unpack-" + Guid.NewGuid().ToString("N");
            try { entry.ExtractToFile(temporary); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (!required.All(name => File.Exists(Path.Combine(AppPaths.Tools, name)))) throw new InvalidDataException("Los componentes incluidos están incompletos.");
        File.WriteAllText(marker, UpdateService.CurrentVersion);
        AppPaths.Log($"Componentes incluidos listos en {watch.ElapsedMilliseconds} ms.");
    }
}
