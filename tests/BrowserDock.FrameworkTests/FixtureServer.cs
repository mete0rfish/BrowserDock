using System.Diagnostics;
using NUnit.Framework;

namespace BrowserDock.FrameworkTests;

internal sealed class FixtureServer : IDisposable
{
    private readonly Process process;
    private readonly Task stdout;
    private readonly Task stderr;
    public Uri Url { get; }
    internal Process Process => process;
    private FixtureServer(Process process, Uri url, Task stderr)
    { this.process = process; Url = url; this.stderr = stderr; stdout = process.StandardOutput.ReadToEndAsync(); }
    public static async Task<FixtureServer> StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BROWSERDOCK_FIXTURE_HOST");
        if (string.IsNullOrEmpty(configured))
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrowserDock.slnx"))) directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Set BROWSERDOCK_FIXTURE_HOST to the fixture host DLL.");
            var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            configured = Path.Combine(directory.FullName, "tests", "BrowserDock.FixtureHost", "bin", configuration, "net10.0", "BrowserDock.FixtureHost.dll");
        }
        if (!File.Exists(configured)) throw new FileNotFoundException("Build the .NET 10 fixture host first.", configured);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        RuntimeCompatibility.SetArguments(info, new[] { configured });
        var process = Process.Start(info) ?? throw new InvalidOperationException("Fixture host did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var line = await TaskCompatibility.WaitAsync(process.StandardOutput.ReadLineAsync(), deadline.Token);
                if (line is null) throw new InvalidOperationException("Fixture host exited before readiness: " + await stderr);
                if (line.StartsWith("BROWSERDOCK_FIXTURE_URL=", StringComparison.Ordinal)) return new(process, new Uri(line.Substring("BROWSERDOCK_FIXTURE_URL=".Length)), stderr);
            }
        }
        catch { if (!process.HasExited) process.Kill(); process.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (!process.HasExited) process.Kill();
        if (!process.WaitForExit(5000)) throw new TimeoutException("Owned fixture host did not exit.");
        if (!Task.WaitAll(new[] { stdout, stderr }, 5000)) throw new TimeoutException("Fixture host pipes did not close.");
        process.Dispose();
    }
}
