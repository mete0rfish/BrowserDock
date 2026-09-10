using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace UcDotNet.Hosting;

internal static class Paths
{
    public static string TempRoot => OperatingSystem.IsMacOS() && System.IO.Path.GetTempPath().StartsWith("/var/", StringComparison.Ordinal)
        ? "/private" + System.IO.Path.GetTempPath() : System.IO.Path.GetTempPath();
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UcException(ErrorCategory.ConfigurationError, "Symbolic links and reparse points are not accepted for owned resources.");
        return Path.TrimEndingDirectorySeparator(full);
    }
}
internal sealed class Profile : IDisposable
{
    private const string Marker = ".ucdotnet-owner.json";
    private readonly FileStream profileLock;
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private bool released;
    public string Path { get; }
    public bool Temporary { get; }
    private Profile(string path, bool temporary, FileStream profileLock) { Path = path; Temporary = temporary; this.profileLock = profileLock; }
    public static Profile Acquire(ProfileOptions options)
    {
        var temporary = options.Directory is null;
        var root = Paths.Canonical(System.IO.Path.Combine(Paths.TempRoot, "UcDotNet"));
        var path = Paths.Canonical(options.Directory ?? System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var vendor in new[] { "Google/Chrome/User Data", "Google/Chrome Beta/User Data", "Google/Chrome SxS/User Data", "Chromium/User Data" })
        {
            var forbidden = System.IO.Path.GetFullPath(System.IO.Path.Combine(local, vendor));
            if (path.Equals(forbidden, StringComparison.OrdinalIgnoreCase) || path.StartsWith(forbidden + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new UcException(ErrorCategory.ConfigurationError, "The default browser profile cannot be used.");
        }
        Directory.CreateDirectory(path);
        FileStream fileLock;
        try { fileLock = new FileStream(System.IO.Path.Combine(path, ".ucdotnet.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new UcException(ErrorCategory.ConfigurationError, "The profile is already in use.", e); }
        var profile = new Profile(path, temporary, fileLock);
        try
        {
            if (temporary) File.WriteAllText(System.IO.Path.Combine(path, Marker), JsonSerializer.Serialize(new { Schema = 1, Nonce = profile.nonce, Path = path }));
            return profile;
        }
        catch { profile.Dispose(); throw; }
    }
    public async Task CleanupAsync(bool chromeExited, CancellationToken token)
    {
        if (!chromeExited) throw new UcException(ErrorCategory.CleanupIncomplete, "Chrome is still alive; the profile remains locked and is not deleted.");
        Dispose();
        if (!Temporary) return;
        var root = Paths.Canonical(System.IO.Path.Combine(Paths.TempRoot, "UcDotNet"));
        if (!Paths.Canonical(Path).StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UcException(ErrorCategory.CleanupIncomplete, "Temporary profile escaped its ownership root.");
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(Path, Marker), token));
        if (marker.RootElement.GetProperty("Schema").GetInt32() != 1 || marker.RootElement.GetProperty("Nonce").GetString() != nonce || marker.RootElement.GetProperty("Path").GetString() != Path)
            throw new UcException(ErrorCategory.CleanupIncomplete, "Profile ownership marker does not match.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { await Task.Run(() => Directory.Delete(Path, true)).WaitAsync(token); return; }
            catch (IOException) { await Task.Delay(100, token); }
            catch (UnauthorizedAccessException) { await Task.Delay(100, token); }
        }
    }
    public void Dispose() { if (!released) { released = true; profileLock.Dispose(); } }
}

internal sealed class OwnedProcess : IDisposable
{
    private readonly Process process;
    private readonly Task stdout;
    private readonly Task stderr;
    private readonly SafeFileHandle? job;
    private readonly string executable;
    public int Id { get; }
    public DateTime Created { get; }
    public bool Alive
    {
        get { try { return !process.HasExited; } catch (InvalidOperationException) { return false; } }
    }
    public bool TreeAlive => Alive || (job is not null && !job.IsClosed && WindowsJob.ActiveProcesses(job) != 0);
    public OwnedProcess(string path, IEnumerable<string> arguments, bool tree = false)
    {
        executable = Paths.Canonical(path);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        if (tree && OperatingSystem.IsWindows())
        {
            (process, job) = WindowsJob.StartSuspended(executable, info.ArgumentList);
            stdout = stderr = Task.CompletedTask; // Native Chrome launch has no inherited console/pipe handles.
        }
        else
        {
            process = Process.Start(info) ?? throw new UcException(ErrorCategory.ChromeStartFailure, "Process creation returned no process.");
            stdout = DrainAsync(process.StandardOutput);
            stderr = DrainAsync(process.StandardError);
        }
        Id = process.Id;
        Created = process.StartTime.ToUniversalTime();
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer) > 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    private void KillExact()
    {
        if (!Alive) return;
        if (process.StartTime.ToUniversalTime() != Created || !string.Equals(Paths.Canonical(process.MainModule!.FileName), executable, StringComparison.OrdinalIgnoreCase))
            throw new UcException(ErrorCategory.CleanupIncomplete, "Process identity changed; refusing termination.");
        process.Kill();
    }
    public void Terminate()
    {
        if (job is not null)
        {
            if (!WindowsJob.TerminateJobObject(job, 1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        else KillExact();
    }
    public async Task WaitAsync(CancellationToken token)
    {
        await process.WaitForExitAsync(token);
        await Task.WhenAll(stdout, stderr).WaitAsync(token);
        if (job is not null)
            while (WindowsJob.ActiveProcesses(job) != 0) await Task.Delay(25, token);
    }
    public void Dispose() { job?.Dispose(); process.Dispose(); }
}
internal static class WindowsJob
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public uint Size; public string? Reserved, Desktop, Title; public uint X, Y, Width, Height, XChars, YChars, Fill, Flags; public ushort Show, ReservedLength; public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long A, B, C, D; public uint E, Total, Active, Terminated; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out Accounting info, uint size, IntPtr returned);
    public static SafeFileHandle Create(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job, process.Handle))
        { job.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        return job;
    }
    public static (Process, SafeFileHandle) StartSuspended(string executable, IEnumerable<string> arguments)
    {
        var command = new System.Text.StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x4 | 0x08000000, IntPtr.Zero, null, ref startup, out var info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Process? process = null; SafeFileHandle? job = null;
        try
        {
            process = Process.GetProcessById((int)info.ProcessId);
            job = Create(process);
            if (ResumeThread(info.Thread) == uint.MaxValue) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return (process, job);
        }
        catch
        {
            job?.Dispose();
            if (process is not null) { if (!process.HasExited) process.Kill(); process.WaitForExit(5000); process.Dispose(); }
            throw;
        }
        finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
    }
    internal static string Quote(string argument)
    {
        var text = new System.Text.StringBuilder("\""); var slashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\') { slashes++; continue; }
            text.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes); text.Append(ch); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static uint ActiveProcesses(SafeFileHandle job)
    {
        if (!QueryInformationJobObject(job, 1, out var info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return info.Active;
    }
}
internal static class BrowserHosting
{
    public static HttpClient Http() => new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static string LocateChrome(string? specified)
    {
        var candidates = specified is not null ? new[] { specified } : new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.Where(File.Exists).Select(Paths.Canonical).FirstOrDefault() ?? throw new UcException(ErrorCategory.ConfigurationError, "Chrome executable was not found.");
    }
    public static async Task<Version> ValidateVersionsAsync(string chrome, string driver, CancellationToken token)
    {
        if (!File.Exists(driver)) throw new UcException(ErrorCategory.ConfigurationError, "ChromeDriver executable was not found.");
        var chromeVersion = ParseVersion(FileVersionInfo.GetVersionInfo(chrome).ProductVersion ?? "");
        var info = new ProcessStartInfo(Paths.Canonical(driver)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--version");
        using var process = Process.Start(info) ?? throw new UcException(ErrorCategory.VersionMismatch, "Unable to read driver version.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); await errors; }
        catch { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); throw; }
        var version = ParseVersion(await output);
        RequireMatchingVersions(chromeVersion, version);
        return version;
    }
    internal static Version ParseVersion(string value)
    {
        var match = Regex.Match(value, @"\d+\.\d+\.\d+\.\d+");
        if (!match.Success) throw new UcException(ErrorCategory.VersionMismatch, "Could not parse a four-part browser/driver version.");
        return Version.Parse(match.Value);
    }
    internal static void RequireMatchingVersions(Version chrome, Version driver)
    {
        if (chrome.Major < 115 || chrome.Major != driver.Major || chrome.Minor != driver.Minor || chrome.Build != driver.Build)
            throw new UcException(ErrorCategory.VersionMismatch, "Chrome and ChromeDriver must match MAJOR.MINOR.BUILD (M115+).");
    }
    public static async Task<Uri> DiscoverAsync(Profile profile, OwnedProcess chrome, CancellationToken token)
    {
        using var http = Http();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!chrome.Alive) throw new BrowserExitedException();
            try
            {
                var lines = await File.ReadAllLinesAsync(Path.Combine(profile.Path, "DevToolsActivePort"), token);
                if (lines.Length >= 2 && int.TryParse(lines[0], out var port) && port is > 0 and <= 65535 && lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal))
                {
                    using var json = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", token));
                    var ws = new Uri(json.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!);
                    if (ws.Scheme == "ws" && ws.IsLoopback && ws.Port == port && ws.AbsolutePath == lines[1] && chrome.Alive && OwnsLoopbackListener(chrome.Id, port))
                        return new UriBuilder(ws) { Host = "127.0.0.1" }.Uri;
                }
            }
            catch (Exception e) when (e is IOException or HttpRequestException or JsonException or KeyNotFoundException or UriFormatException) { }
            await Task.Delay(50, token);
        }
    }
    public static int CandidatePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    public static bool OwnsLoopbackListener(int pid, int port)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0) return false;
            var count = Marshal.ReadInt32(buffer);
            for (var row = 0; row < count; row++)
            {
                var offset = 4 + row * 24;
                var address = Marshal.ReadInt32(buffer, offset + 4);
                var networkPort = Marshal.ReadInt32(buffer, offset + 8);
                var localPort = ((networkPort & 0xff) << 8) | ((networkPort >> 8) & 0xff);
                if (localPort == port && address == 0x0100007f && Marshal.ReadInt32(buffer, offset + 20) == pid) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
