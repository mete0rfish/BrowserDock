using BrowserDock.TestFixtures;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ReferenceEndpointReadinessTests
{
    private const string Target = "/devtools/browser/8d4f7e2a-0861-4446-b704-bf9ce1eca2ce";
    private const string Ready = "9222\n" + Target;

    [TestCase("")]
    [TestCase("9222")]
    [TestCase("9222\n")]
    [TestCase("bad\n" + Target)]
    [TestCase("0\n" + Target)]
    [TestCase("65536\n" + Target)]
    [TestCase("9222\n/devtools/page/8d4f7e2a-0861-4446-b704-bf9ce1eca2ce")]
    [TestCase("9222\n/devtools/browser/8d4f7e2a-0861-")]
    [TestCase("9222\n" + Target + "?unexpected")]
    public async Task IncompleteOrInvalidContentsArePolledUntilReady(string initial)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reads = 0;
        var endpoint = await ReferenceEndpointReadiness.WaitAsync("controlled", () => true, deadline.Token,
            _ => ++reads == 1 ? initial : Ready);
        Assert.That(endpoint.AbsoluteUri, Is.EqualTo("ws://127.0.0.1:9222" + Target));
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public async Task MissingFileIsPolledUntilPublished()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reads = 0;
        var endpoint = await ReferenceEndpointReadiness.WaitAsync("controlled", () => true, deadline.Token,
            _ => ++reads == 1 ? throw new FileNotFoundException() : Ready);
        Assert.That(endpoint.Port, Is.EqualTo(9222));
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public async Task DefaultReaderAcceptsActualFileWithWindowsNewlines()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Ready.Replace("\n", "\r\n") + "\r\n");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var endpoint = await ReferenceEndpointReadiness.WaitAsync(path, () => true, deadline.Token);
            Assert.That(endpoint.AbsolutePath, Is.EqualTo(Target));
        }
        finally { File.Delete(path); }
    }

    [TestCase("")]
    [TestCase(Ready)]
    public void ChromeExitDuringReadRejectsEvenCompleteContents(string contents)
    {
        var alive = true; var reads = 0;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => alive, CancellationToken.None, _ => { reads++; alive = false; return contents; }));
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public void ChromeExitBeforeReadFailsImmediately()
    {
        var reads = 0;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => false, CancellationToken.None, _ => { reads++; return Ready; }));
        Assert.That(reads, Is.Zero);
    }

    [Test]
    public void ChromeExitBetweenPollsFailsWithoutAnotherRead()
    {
        var checks = 0; var reads = 0;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => ++checks < 3, CancellationToken.None, _ => { reads++; return ""; }));
        Assert.That(reads, Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase(Ready)]
    public void CancellationDuringReadPreservesCallerToken(string contents)
    {
        using var caller = new CancellationTokenSource();
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => true, caller.Token, _ => { caller.Cancel(); return contents; }));
        Assert.That(error!.CancellationToken, Is.EqualTo(caller.Token));
    }

    [Test]
    public void CancellationDuringPollingUsesOriginalDeadline()
    {
        using var caller = new CancellationTokenSource();
        var reads = 0;
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => true, caller.Token, _ => { if (++reads == 2) caller.Cancel(); return ""; }));
        Assert.That(error!.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public void UnrelatedIoFailureIsNotRetried()
    {
        var original = new IOException("controlled unrelated I/O"); var reads = 0;
        var error = Assert.ThrowsAsync<IOException>(async () => await ReferenceEndpointReadiness.WaitAsync(
            "controlled", () => true, CancellationToken.None, _ => { reads++; throw original; }));
        Assert.That(error, Is.SameAs(original));
        Assert.That(reads, Is.EqualTo(1));
    }
}
