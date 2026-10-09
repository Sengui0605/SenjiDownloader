using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace SenjiDownloader;

public static class ProcessRunner
{
    internal static bool TraceProcesses;
    public static async Task<string> Run(string executable, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken token)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Falta un componente de la carpeta tools. Extrae la distribución completa.", executable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.Base
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["DENO_NO_UPDATE_CHECK"] = "1";
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        token.ThrowIfCancellationRequested();
        process.Start();
        using var childJob = new ChildProcessJob(process);
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        var tail = new Queue<string>(); var gate = new object();
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (gate) { tail.Enqueue(line); while (tail.Count > 24) tail.Dequeue(); }
                if (TraceProcesses) AppPaths.Log(line);
                onLine?.Invoke(line);
            }
        }
        await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync());
        token.ThrowIfCancellationRequested();
        string output; lock (gate) output = string.Join(Environment.NewLine, tail);
        if (process.ExitCode != 0) throw new InvalidOperationException(output.Length > 0 ? output : $"El motor terminó con código {process.ExitCode}.");
        return output;
    }
}

// Children die with the app, including FFmpeg spawned by the download engine.
internal sealed class ChildProcessJob : IDisposable
{
    private IntPtr handle;
    public ChildProcessJob(Process process)
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return;
        var info = new ExtendedLimitInformation(); info.Basic.LimitFlags = 0x2000;
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimitInformation>());
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(handle, 9, pointer, (uint)Marshal.SizeOf<ExtendedLimitInformation>()) || !AssignProcessToJobObject(handle, process.Handle)) Dispose();
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic; public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
