using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class DiagnosticsTests
{
    [Test]
    public async Task HttpEvidenceIsBoundedRedactedAndRecordsRequestCancellation()
    {
        var trace = new TestFixtures.HttpRequestTrace();
        var context = new DefaultHttpContext();
        context.Request.Path = "/page";
        context.Request.QueryString = new("?secret-query");
        context.Request.Headers.Authorization = "secret-header";
        for (var i = 0; i < 40; i++) await trace.InvokeAsync(context, () => Task.CompletedTask);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        context.RequestAborted = canceled.Token;
        Assert.CatchAsync<OperationCanceledException>(async () => await trace.InvokeAsync(context, () => Task.FromCanceled(canceled.Token)));
        var records = trace.Snapshot();
        Assert.That(records, Has.Length.EqualTo(64));
        Assert.That(string.Join("", records), Does.Not.Contain("secret-"));
        using var last = JsonDocument.Parse(records[records.Length - 1]);
        Assert.That(last.RootElement.GetProperty("phase").GetString(), Is.EqualTo("end"));
        Assert.That(last.RootElement.GetProperty("canceled").GetBoolean(), Is.True);
        Assert.That(last.RootElement.GetProperty("exception").GetString(), Is.EqualTo("TaskCanceledException"));
    }

    [Test]
    public async Task BlockedSinkDoesNotBlockWriterAndOverflowIsReportedAfterRelease()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var events = new ConcurrentQueue<int>();
        var logger = new Sink((id, _) => { entered.TrySetResult(true); release.Wait(); events.Enqueue(id); });
        var diagnostics = new Diagnostics(logger);
        Task? writer = null;
        try
        {
            diagnostics.Write(42, "first");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Keep the writer independent of the pool thread deliberately blocked in the sink.
            writer = Task.Factory.StartNew(() =>
            {
                writerStarted.SetResult(true);
                for (var i = 0; i < 2000; i++) diagnostics.Write(43, "queued");
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await writer.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(events, Is.Empty, "The writer must finish while the sink is still blocked.");
        }
        finally
        {
            release.Set();
            try { if (writer is not null) await writer.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await diagnostics.DisposeAsync(); }
        }
        Assert.That(events, Does.Contain(1099));
        Assert.That(diagnostics.ActiveWorkersForTest, Is.Zero);
    }

    [Test]
    public async Task ThrowingSinkDoesNotFaultDrainOrLeakWorker()
    {
        var calls = 0;
        var diagnostics = new Diagnostics(new Sink((_, _) => { Interlocked.Increment(ref calls); throw new InvalidOperationException("sink failure"); }));
        for (var i = 0; i < 20; i++) diagnostics.Write(42, "safe metadata");
        await diagnostics.DisposeAsync();
        Assert.That(calls, Is.GreaterThan(0));
        Assert.That(diagnostics.ActiveWorkersForTest, Is.Zero);
    }

    private sealed class Sink(Action<int, string> log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => log(id.Id, formatter(state, exception));
    }
}
