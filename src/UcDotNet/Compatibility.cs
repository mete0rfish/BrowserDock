using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UcDotNet;

// These implementations are also compiled on modern .NET so their race/error
// semantics can be tested on every development host.
internal static class TaskCompatibility
{
    public static Task WaitAsync(this Task task, CancellationToken token) => WaitAsync(task, Timeout.InfiniteTimeSpan, token);
    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken token = default)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!task.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            using var timer = new CancellationTokenSource();
            var wake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var canceled = token.Register(() => wake.TrySetResult(true));
            using var elapsed = timer.Token.Register(() => wake.TrySetResult(true));
            timer.CancelAfter(timeout);
            await Task.WhenAny(task, wake.Task).ConfigureAwait(false);
            if (!task.IsCompleted)
            {
                // Waiting cancellation does not cancel the underlying operation.
                // Its owner is responsible for stopping I/O/processes.
                _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
        }
        await task.ConfigureAwait(false);
    }
    public static Task<T> WaitAsync<T>(this Task<T> task, CancellationToken token) => WaitAsync(task, Timeout.InfiniteTimeSpan, token);
    public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken token = default)
    {
        await WaitAsync((Task)task, timeout, token).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
    public static async Task WaitForExitAsync(this Process process, CancellationToken token = default)
    {
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => exited.TrySetResult(true);
        process.Exited += handler;
        try
        {
            process.EnableRaisingEvents = true;
            // Handles exit before subscription or between subscription and enabling events.
            if (process.HasExited) exited.TrySetResult(true);
            await WaitAsync(exited.Task, token).ConfigureAwait(false);
        }
        finally { process.Exited -= handler; }
    }
}

internal static class RuntimeCompatibility
{
    public static void NotNull(object? value, string parameter)
    { if (value is null) throw new ArgumentNullException(parameter); }
    public static void NotDisposed(bool disposed, object owner)
    { if (disposed) throw new ObjectDisposedException(owner.GetType().FullName); }
    public static string TrimDirectorySeparator(string path)
    {
        var rootLength = (Path.GetPathRoot(path) ?? "").Length;
        var end = path.Length;
        while (end > rootLength && (path[end - 1] == Path.DirectorySeparatorChar || path[end - 1] == Path.AltDirectorySeparatorChar)) end--;
        return path.Substring(0, end);
    }
    public static void SetArguments(ProcessStartInfo info, IEnumerable<string> arguments)
    {
#if NETFRAMEWORK
        info.Arguments = string.Join(" ", arguments.Select(Hosting.WindowsJob.Quote));
#else
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
#endif
    }
    public static async Task<string> ReadAllTextAsync(string path, CancellationToken token)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        using var reader = new StreamReader(file);
        return await TaskCompatibility.WaitAsync(reader.ReadToEndAsync(), token).ConfigureAwait(false);
    }
    public static async Task<string[]> ReadAllLinesAsync(string path, CancellationToken token)
    {
        var text = await ReadAllTextAsync(path, token).ConfigureAwait(false);
        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null) lines.Add(line);
        return lines.ToArray();
    }
    public static async Task<string> HttpStringAsync(HttpClient http, string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    public static Task<string> ReadToEndAsync(StreamReader reader, CancellationToken token) => TaskCompatibility.WaitAsync(reader.ReadToEndAsync(), token);
    public static string Sha256(byte[] bytes)
    {
        using var algorithm = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
    }
#if NETFRAMEWORK
    public static bool Contains(this string text, string value, StringComparison comparison) => text.IndexOf(value, comparison) >= 0;
#endif
}

internal static class Platform
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsX64Process => Environment.Is64BitProcess && RuntimeInformation.OSArchitecture == Architecture.X64;
    public static int WindowsBuild
    {
        get
        {
            if (!IsWindows) return 0;
            var version = new OsVersion { Size = (uint)Marshal.SizeOf<OsVersion>() };
            if (RtlGetVersion(ref version) != 0) throw new UcException(ErrorCategory.ConfigurationError, "Could not determine the Windows version.");
            return (int)version.Build;
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersion
    {
        public uint Size, Major, Minor, Build, PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? ServicePack;
    }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
}
