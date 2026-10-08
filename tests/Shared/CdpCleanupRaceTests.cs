using BrowserDock.Cdp;
using BrowserDock.TestFixtures;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class CdpCleanupRaceTests
{
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
            Assert.That(evidence, Does.Contain("4.0."));
            Assert.That(evidence, Does.Not.Contain("stackPointer").And.Not.Contain("instructionPointer"));
        }
        finally { release.Set(); await TaskCompatibility.WaitAsync(worker, TimeSpan.FromSeconds(15)); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StackProbeBoundary(TaskCompletionSource<bool> entered, ManualResetEventSlim release)
    { entered.TrySetResult(true); release.Wait(); }
#endif
}
