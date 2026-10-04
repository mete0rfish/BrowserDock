using System.Text.Json;
using BrowserDock.Cdp;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class UcScriptRegistrationTests
{
    [Test]
    public async Task NewDriverAttachmentCanRestoreOrderWithoutDuplicatingSources()
    {
        var registration = new UcScriptRegistration(); var fixture = new ScriptAgent();
        await registration.ReconcileAsync(new[] { "cleanup", "caller" }, fixture.Send, fixture.Invalidate, CancellationToken.None);
        fixture.Events.Clear();
        await registration.ReconcileAsync(new[] { "cleanup", "caller" }, fixture.Send, fixture.Invalidate, CancellationToken.None, forceOrder: true);
        Assert.That(fixture.Events, Is.EqualTo(new[] { "remove:1", "remove:2", "add:cleanup", "add:caller" }));
        Assert.That(fixture.Active.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ChangedCdcCleanupReplacesTheOrderedSetWithoutAccumulatingScripts()
    {
        var registration = new UcScriptRegistration();
        var fixture = new ScriptAgent();
        await registration.ReconcileAsync(new[] { "profile", "caller" }, fixture.Send, fixture.Invalidate, CancellationToken.None);
        fixture.Events.Clear();
        await registration.ReconcileAsync(new[] { "cleanup", "profile", "caller" }, fixture.Send, fixture.Invalidate, CancellationToken.None);
        Assert.That(fixture.Events, Is.EqualTo(new[] { "remove:1", "remove:2", "add:cleanup", "add:profile", "add:caller" }));
        Assert.That(fixture.Active.Count, Is.EqualTo(3));
        fixture.Events.Clear();
        await registration.ReconcileAsync(new[] { "cleanup", "profile", "caller" }, fixture.Send, fixture.Invalidate, CancellationToken.None);
        Assert.That(fixture.Events, Is.Empty, "Repeated preparation must not register duplicates.");
        Assert.That(fixture.Invalidations, Is.Zero);
    }

    [TestCase(true), TestCase(false)]
    public async Task UncertainMutationInvalidatesTheTransportInsteadOfRetrying(bool removing)
    {
        var registration = new UcScriptRegistration(); var fixture = new ScriptAgent();
        if (removing) await registration.ReconcileAsync(new[] { "old" }, fixture.Send, fixture.Invalidate, CancellationToken.None);
        using var canceled = new CancellationTokenSource();
        fixture.AfterMutation = () => { canceled.Cancel(); throw new OperationCanceledException(canceled.Token); };
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await registration.ReconcileAsync(new[] { "new" }, fixture.Send, fixture.Invalidate, canceled.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(canceled.Token));
        Assert.That(fixture.Invalidations, Is.EqualTo(1));
        var count = fixture.Events.Count;
        Assert.ThrowsAsync<BrowserDockException>(async () => await registration.ReconcileAsync(new[] { "new" }, fixture.Send, fixture.Invalidate, CancellationToken.None));
        Assert.That(fixture.Events.Count, Is.EqualTo(count));
    }

    [Test]
    public void PreCanceledRegistrationDoesNotInvalidateHealthyTransport()
    {
        var registration = new UcScriptRegistration(); var fixture = new ScriptAgent();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await registration.ReconcileAsync(new[] { "new" }, fixture.Send, fixture.Invalidate, canceled.Token));
        Assert.That(fixture.Events, Is.Empty);
        Assert.That(fixture.Invalidations, Is.Zero);
    }

    [Test]
    public void MissingIdentifierCannotBecomeASuccessfulCachedRegistration()
    {
        var registration = new UcScriptRegistration(); var fixture = new ScriptAgent { OmitIdentifier = true };
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await registration.ReconcileAsync(new[] { "new" }, fixture.Send, fixture.Invalidate, CancellationToken.None));
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.ProtocolError));
        Assert.That(fixture.Invalidations, Is.EqualTo(1));
    }

    [Test]
    public async Task SessionDetachmentDuringRegistrationCannotRepopulateItsCache()
    {
        var registration = new UcScriptRegistration(); var fixture = new ScriptAgent();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<JsonElement> Send(string method, object args, CancellationToken token)
        { var result = await fixture.Send(method, args, token); entered.TrySetResult(true); await release.Task; return result; }
        var pending = registration.ReconcileAsync(new[] { "first", "second" }, Send, fixture.Invalidate, CancellationToken.None);
        try
        {
            await TaskCompatibility.WaitAsync(entered.Task, TimeSpan.FromSeconds(5));
            registration.Detach(); release.TrySetResult(true);
            Assert.ThrowsAsync<BrowserDockException>(async () => await TaskCompatibility.WaitAsync(pending, TimeSpan.FromSeconds(5)));
            Assert.That(fixture.Events, Is.EqualTo(new[] { "add:first" }));
            Assert.That(fixture.Invalidations, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); }
    }

    private sealed class ScriptAgent
    {
        public readonly Dictionary<string, string> Active = new();
        public readonly List<string> Events = new();
        public int Invalidations;
        public bool OmitIdentifier;
        public Action? AfterMutation;
        private int sequence;
        public void Invalidate() => Invalidations++;
        public Task<JsonElement> Send(string method, object args, CancellationToken token)
        {
            var parameters = JsonSerializer.SerializeToElement(args);
            object reply = new { };
            if (method == "Page.addScriptToEvaluateOnNewDocument")
            {
                var id = (++sequence).ToString(); var source = parameters.GetProperty("source").GetString()!;
                Active.Add(id, source); Events.Add("add:" + source);
                if (!OmitIdentifier) reply = new { identifier = id };
            }
            else
            {
                var id = parameters.GetProperty("identifier").GetString()!;
                Assert.That(Active.Remove(id), Is.True); Events.Add("remove:" + id);
            }
            AfterMutation?.Invoke();
            return Task.FromResult(JsonSerializer.SerializeToElement(reply));
        }
    }
}
