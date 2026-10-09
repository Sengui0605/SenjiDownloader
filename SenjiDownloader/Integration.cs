using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32;

namespace SenjiDownloader;

public static class DesktopIntegration
{
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("SenjiDownloader", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("SenjiDownloader", false);
    }
    public static void CreateShortcut()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("No se pudo acceder al escritorio de Windows.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SenjiDownloader.lnk");
            dynamic shortcut = shell.CreateShortcut(path);
            try
            {
                shortcut.TargetPath = Environment.ProcessPath!;
                shortcut.WorkingDirectory = AppPaths.Base;
                shortcut.IconLocation = Environment.ProcessPath! + ",0";
                shortcut.Description = "SenjiDownloader — descargas y conversiones";
                shortcut.Save();
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { Path.GetFullPath(path) } });
    }
    public static void Reveal(string path)
    {
        if (File.Exists(path))
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(Path.GetFullPath(path)); Process.Start(start);
        }
        else OpenFolder(Path.GetDirectoryName(path) ?? AppPaths.DefaultDownloads);
    }
}

public sealed class SingleInstance : IDisposable
{
    private static string User => WindowsIdentity.GetCurrent().User?.Value.Replace('-', '_') ?? Environment.UserName;
    private static string Pipe => "SenjiDownloader_" + User;
    public static string UpdateReady => @"Local\SenjiDownloaderUpdate_" + User;
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    public bool Owns { get; }
    public SingleInstance() { mutex = new Mutex(true, @"Local\" + Pipe, out var created); Owns = created; }
    public static bool Send(string message)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Pipe, PipeDirection.Out);
            pipe.Connect(2500); using var writer = new StreamWriter(pipe); writer.WriteLine(message); writer.Flush(); return true;
        }
        catch { return false; }
    }
    public async Task Listen(Action<string> received)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe);
                var command = await reader.ReadLineAsync(stop.Token);
                if (command != null) received(command);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AppPaths.Log("Comunicación de instancia: " + ex.Message); await Task.Delay(500); }
        }
    }
    public void Dispose() { stop.Cancel(); if (Owns) mutex.ReleaseMutex(); mutex.Dispose(); }
}
