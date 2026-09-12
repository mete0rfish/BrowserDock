using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using NUnit.Framework;
using BrowserDock.WebDriver;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class AttachmentFailureTests
{
    [TestCase(false), TestCase(true)]
    public async Task StalledNewSessionTerminatesOwnedDriverAndListener(bool cancelCaller)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || Environment.OSVersion.Version.Build < 22000)
            Assert.Ignore("Requires Windows 11 x64 process ownership probes.");
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrowserDock.slnx"))) directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        var executable = Path.Combine(directory!.FullName, "tests", "BrowserDock.FixtureHost", "bin", configuration, "net10.0", "BrowserDock.FixtureHost.exe");
        Assert.That(File.Exists(executable), Is.True, "Build the Windows fixture host apphost first.");
        var report = Path.Combine(Path.GetTempPath(), "BrowserDock-stalled-" + Guid.NewGuid().ToString("N") + ".json");
        var previous = Environment.GetEnvironmentVariable("BROWSERDOCK_FAKE_DRIVER_REPORT");
        Environment.SetEnvironmentVariable("BROWSERDOCK_FAKE_DRIVER_REPORT", report);
        using var caller = new CancellationTokenSource();
        Task<Attachment>? creating = null;
        try
        {
            creating = Attachment.CreateAsync(executable, new Uri("ws://127.0.0.1:1/devtools/browser/fixture"),
                new() { WebDriverSessionCreate = TimeSpan.FromSeconds(3), Command = TimeSpan.FromSeconds(30) }, caller.Token);
            using var readiness = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(report))
            {
                if (creating.IsCompleted) { var unexpected = await creating; await unexpected.DestroyAsync(readiness.Token); Assert.Fail("Session completed before the stalled response was reached."); }
                await Task.Delay(20, readiness.Token);
            }
            var timer = Stopwatch.StartNew();
            if (cancelCaller) caller.Cancel();
            var error = Assert.CatchAsync<Exception>(async () => await creating.WaitAsync(TimeSpan.FromSeconds(13)));
            if (cancelCaller) Assert.That(error, Is.InstanceOf<OperationCanceledException>());
            else Assert.That(error, Is.TypeOf<BrowserDockException>().And.Property(nameof(BrowserDockException.Category)).EqualTo(ErrorCategory.WebDriverSessionFailure));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(13)));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            var pid = json.RootElement.GetProperty("pid").GetInt32();
            try { using var owned = Process.GetProcessById(pid); Assert.That(owned.HasExited, Is.True); }
            catch (ArgumentException) { }
            using var client = new TcpClient();
            Assert.ThrowsAsync<SocketException>(async () => await client.ConnectAsync("127.0.0.1", json.RootElement.GetProperty("port").GetInt32()).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            caller.Cancel();
            try
            {
                if (creating is not null)
                {
                    // Also drain on fixture-readiness failure, before reading any report.
                    try { var late = await creating.WaitAsync(TimeSpan.FromSeconds(10)); using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await late.DestroyAsync(cleanup.Token); }
                    catch (Exception e) when (e is BrowserDockException or OperationCanceledException) { }
                }
            }
            finally { Environment.SetEnvironmentVariable("BROWSERDOCK_FAKE_DRIVER_REPORT", previous); File.Delete(report); File.Delete(report + ".tmp"); }
        }
    }
}
