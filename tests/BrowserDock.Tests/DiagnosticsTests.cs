using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class DiagnosticsTests
{
    [Test]
    public async Task BlockedSinkDoesNotBlockWriterAndOverflowIsReportedAfterRelease()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var events = new ConcurrentQueue<int>();
        var logger = new Sink((id, _) => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); events.Enqueue(id); });
        var diagnostics = new Diagnostics(logger);
        try
        {
            diagnostics.Write(42, "first");
            Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);
            await Task.Run(() => { for (var i = 0; i < 2000; i++) diagnostics.Write(43, "queued"); }).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); await diagnostics.DisposeAsync(); }
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
