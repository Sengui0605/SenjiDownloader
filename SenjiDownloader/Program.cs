using System.Diagnostics;

namespace SenjiDownloader;

internal static class Program
{
    public static readonly Stopwatch Startup = Stopwatch.StartNew();
    public static bool NoIntegration { get; private set; }
    [STAThread]
    private static int Main(string[] args)
    {
        Startup.Restart();
        NoIntegration = args.Contains("--no-integration");
        if (args.FirstOrDefault() == "--update-helper") return UpdateService.RunHelper(args);
        var preview = args.FirstOrDefault() is "--render-preview" or "--preview-ui" or "--startup-probe";
        var selfTest = args.FirstOrDefault() == "--self-test";
        AppPaths.Initialize(preview || selfTest ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "test-data") : null);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
        {
            AppPaths.Log(e.Exception.ToString());
            if (preview || selfTest) Application.Exit();
            else MessageBox.Show("Ocurrió un error. Consulta el registro en Preferencias.", "SenjiDownloader");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppPaths.Log(e.ExceptionObject.ToString() ?? "Error inesperado.");
        try
        {
            if (selfTest) return SelfTests.Run(args[1]).GetAwaiter().GetResult();
            if (args.Contains("--quit")) return SingleInstance.Send("EXIT") ? 0 : 1;
            using var instance = preview ? null : new SingleInstance();
            if (instance?.Owns == false) { if (!args.Contains("--background")) SingleInstance.Send("SHOW"); return 0; }
            var settings = preview ? new Preferences() : Storage.Load("preferences.json", () => new Preferences());
            if (settings.DownloadFolder.Length == 0 || !Directory.Exists(settings.DownloadFolder)) settings.DownloadFolder = AppPaths.DefaultDownloads;
            settings.ParallelDownloads = Math.Clamp(settings.ParallelDownloads, 1, 6);
            if (string.IsNullOrEmpty(settings.EngineVersion))
            {
                try
                {
                    using var versions = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppPaths.Tools, "versions.json")));
                    settings.EngineVersion = versions.RootElement.GetProperty("ytDlp").GetString() ?? "incluido";
                    settings.DenoVersion = versions.RootElement.GetProperty("deno").GetString() ?? "";
                }
                catch { settings.EngineVersion = "incluido"; }
            }
            using var form = new MainForm(settings, instance, args.Contains("--background"), preview);
            if (args.FirstOrDefault() == "--render-preview") form.PreviewOutput = Path.GetFullPath(args[1]);
            if (args.FirstOrDefault() == "--startup-probe") form.StartupReport = Path.GetFullPath(args[1]);
            Application.Run(form); return 0;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex.ToString());
            if (!preview && !selfTest) MessageBox.Show("No se pudo iniciar SenjiDownloader:\n" + ex.Message, "SenjiDownloader");
            return 1;
        }
    }
}
