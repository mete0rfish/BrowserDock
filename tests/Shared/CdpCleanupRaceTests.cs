using BrowserDock.Cdp;
using BrowserDock.TestFixtures;
using NUnit.Framework;
using System.Text.Json;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class CdpCleanupRaceTests
{
    [TestCase(false), TestCase(true)]
    public async Task DisposalCompletesWhenReceiveAndOptionalCommandArePending(bool pendingCommand)
    {
        using var server = await FrameworkTests.FixtureServer.StartAsync();
        var endpoint = new UriBuilder(server.Url) { Scheme = "ws", Path = "/cdp",
            Query = "scenario=" + (pendingCommand ? "cleanup-race" : "hold") }.Uri;
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var connection = new CdpConnection();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<CdpEvent> observe = _ => ready.TrySetResult(true);
            if (pendingCommand) connection.Event += observe;
            await connection.ConnectAsync(endpoint, startup.Token);
            var command = pendingCommand ? connection.SendAsync("Fixture.pending", null, null, startup.Token) : null;
            if (pendingCommand)
            {
                await TaskCompatibility.WaitAsync(ready.Task, TimeSpan.FromSeconds(15));
                connection.Event -= observe;
            }
            // The outer watchdog must remain callable even if synchronous native
            // cancellation stalls before DisposeAsync returns its first task.
            var disposal = Task.Run(async () => await connection.DisposeAsync());
            try
            {
                await TaskCompatibility.WaitAsync(disposal, TimeSpan.FromSeconds(15));
                if (command is not null)
                {
                    var error = Assert.ThrowsAsync<BrowserDockException>(async () =>
                        await TaskCompatibility.WaitAsync(command, TimeSpan.FromSeconds(15)));
                    Assert.That(error!.Category, Is.EqualTo(ErrorCategory.DevToolsEndpointFailure));
                }
                Assert.That(connection.ResourcesForTest, Is.EqualTo((0, 0, 0, false)), "iteration " + iteration);
            }
            catch (Exception error)
            {
                connection.RecordCleanup(error); CleanupThreadEvidence.Capture(error);
                TestFixtures.FailureEvidence.Write(error);
                throw;
            }
        }
    }

    [Test]
    public async Task TransportInterruptionAndDisposalCompleteWithPendingReceive()
    {
        using var server = await FrameworkTests.FixtureServer.StartAsync();
        var endpoint = new UriBuilder(server.Url) { Scheme = "ws", Path = "/cdp" }.Uri;
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var connection = new CdpConnection();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await connection.ConnectAsync(endpoint, startup.Token);
            Assert.That(connection.ResourcesForTest.Tasks, Is.EqualTo(2));
            using var release = new ManualResetEventSlim();
            var entered = new CountdownEvent(2);
            var interrupted = Task.Run(() =>
            {
                entered.Signal(); release.Wait();
                try { connection.InterruptForTest(); }
                catch (ObjectDisposedException) { /* A late injected failure after complete disposal owns no resources. */ }
            });
            var disposed = Task.Run(async () => { entered.Signal(); release.Wait(); await connection.DisposeAsync(); });
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(15)), Is.True);
                release.Set();
                await TaskCompatibility.WaitAsync(Task.WhenAll(interrupted, disposed), TimeSpan.FromSeconds(15));
                Assert.That(connection.ResourcesForTest, Is.EqualTo((0, 0, 0, false)), "iteration " + iteration);
            }
            catch (Exception error)
            {
                connection.RecordCleanup(error); CleanupThreadEvidence.Capture(error);
                TestFixtures.FailureEvidence.Write(error);
                throw;
            }
            finally { release.Set(); entered.Dispose(); }
        }
    }

    [Test]
    public async Task DispatcherFailureAndConcurrentShutdownCompleteWithPendingCommand()
    {
        using var server = await FrameworkTests.FixtureServer.StartAsync();
        var endpoint = new UriBuilder(server.Url) { Scheme = "ws", Path = "/cdp", Query = "scenario=cleanup-race" }.Uri;
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var connection = new CdpConnection();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var release = new ManualResetEventSlim();
            using var entered = new CountdownEvent(4);
            connection.Event += _ => { entered.Signal(); release.Wait(); throw new IOException("Controlled dispatcher failure."); };
            await connection.ConnectAsync(endpoint, startup.Token);
            var command = connection.SendAsync("Fixture.pending", null, null, startup.Token);
            Task Interrupt() => Task.Run(() =>
            {
                entered.Signal(); release.Wait();
                try { connection.InterruptForTest(); }
                catch (ObjectDisposedException) { /* The injected caller may arrive after complete disposal. */ }
            });
            var first = Interrupt(); var second = Interrupt();
            var disposed = Task.Run(async () => { entered.Signal(); release.Wait(); await connection.DisposeAsync(); });
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(connection.ResourcesForTest.Pending, Is.EqualTo(1));
                release.Set();
                await TaskCompatibility.WaitAsync(Task.WhenAll(first, second, disposed), TimeSpan.FromSeconds(15));
                var error = Assert.ThrowsAsync<BrowserDockException>(async () =>
                    await TaskCompatibility.WaitAsync(command, TimeSpan.FromSeconds(15)));
                Assert.That(error!.Category, Is.EqualTo(ErrorCategory.DevToolsEndpointFailure));
                Assert.That(connection.ResourcesForTest.Pending, Is.Zero);
                Assert.That(connection.ResourcesForTest.Tasks, Is.Zero);
                Assert.That(connection.ResourcesForTest.SocketOpen, Is.False);
            }
            catch (Exception error)
            {
                connection.RecordCleanup(error); CleanupThreadEvidence.Capture(error);
                TestFixtures.FailureEvidence.Write(error);
                throw;
            }
            finally { release.Set(); }
        }
    }

#if NETFRAMEWORK
    [Test]
    public async Task CollectorFindsBlockedFrameworkThreadWithoutReadingLocals()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var worker = Task.Run(() => StackProbeBoundary(entered, release));
        try
        {
            await TaskCompatibility.WaitAsync(entered.Task, TimeSpan.FromSeconds(15));
            var evidence = CleanupThreadEvidence.Collect();
            Assert.That(evidence, Does.Contain(nameof(StackProbeBoundary)));
            using var json = JsonDocument.Parse(evidence);
            Assert.That(json.RootElement.EnumerateArray().Any(runtime =>
                Version.Parse(runtime.GetProperty("runtime").GetString()!).Major == 4), Is.True,
                "The collector must find the owned .NET Framework CLR, independent of its servicing version.");
            Assert.That(evidence, Does.Not.Contain("stackPointer").And.Not.Contain("instructionPointer"));
        }
        finally { release.Set(); await TaskCompatibility.WaitAsync(worker, TimeSpan.FromSeconds(15)); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StackProbeBoundary(TaskCompletionSource<bool> entered, ManualResetEventSlim release)
    { entered.TrySetResult(true); release.Wait(); }
#endif
}
