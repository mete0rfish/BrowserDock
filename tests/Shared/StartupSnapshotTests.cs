using System.Text.Json;
using BrowserDock.Patching;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class StartupSnapshotTests
{
    private BrowserOptions options = null!;

    [SetUp]
    public void RequireWindowsFixture()
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000)
            Assert.Ignore("Requires Windows 11 x64 with an interactive desktop.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        if (!File.Exists(chrome) || !File.Exists(driver))
            Assert.Ignore("Set BROWSERDOCK_CHROME and BROWSERDOCK_DRIVER to a compatible fixture pair.");
        options = new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! } };
    }

    [Test]
    public async Task CallerMutationAfterCaptureDoesNotChangeLaunchOrScripts()
    {
        var arguments = new List<string> { "--enable-automation", "--lang=en-US" };
        var scripts = new List<string> { "globalThis.__snapshotValue='captured'" };
        var schemes = new List<string> { "snapshot-before" };
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var starting = Browser.StartAsync(options with
        {
            ChromeArguments = arguments, NewDocumentScripts = scripts, AdditionalUrlSchemes = schemes,
            StageHook = async (stage, token) =>
            {
                if (stage != "validation") return;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
        }, deadline.Token).AsTask();
        Browser? browser = null;
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            arguments[1] = "--lang=ko";
            scripts[0] = "globalThis.__snapshotValue='mutated'";
            schemes[0] = "snapshot-after";
            release.TrySetResult(true);
            browser = await starting;

            var commandLine = await browser.ExecuteCdpAsync("Browser.getBrowserCommandLine", cancellationToken: deadline.Token);
            var actualArguments = commandLine.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
            await browser.NavigateAsync(new Uri("data:text/html,<title>snapshot</title>"), cancellationToken: deadline.Token);
            var initialValue = await ReadValue(browser, deadline.Token);

            // Reinstall after disconnect and CDP recovery, after another caller mutation.
            scripts[0] = "globalThis.__snapshotValue='after-start'";
            await browser.DisconnectWebDriverAsync(deadline.Token);
            browser.InterruptCdpForTest();
            await browser.NavigateAsync(new Uri("data:text/html,<title>recovered</title>"),
                new() { Mode = NavigationMode.CdpOnly }, deadline.Token);
            var recoveredValue = await ReadValue(browser, deadline.Token);
            Assert.Multiple(() =>
            {
                Assert.That(actualArguments, Does.Contain("--lang=en-US").And.Not.Contain("--lang=ko"));
                Assert.That(browser.EffectiveOptionsForTest.ChromeArguments, Is.EqualTo(new[] { "--enable-automation", "--lang=en-US" }));
                Assert.That(browser.EffectiveOptionsForTest.NewDocumentScripts, Is.EqualTo(new[] { "globalThis.__snapshotValue='captured'" }));
                Assert.That(browser.EffectiveOptionsForTest.AdditionalUrlSchemes, Is.EqualTo(new[] { "snapshot-before" }));
                Assert.That(initialValue, Is.EqualTo("captured"));
                Assert.That(recoveredValue, Is.EqualTo("captured"));
                Assert.DoesNotThrow(() => browser.ValidateUrl(new Uri("snapshot-before://host")));
                Assert.Throws<BrowserDockException>(() => browser.ValidateUrl(new Uri("snapshot-after://host")));
            });
        }
        finally
        {
            deadline.Cancel();
            release.TrySetResult(true);
            // Drain a late startup before disposing so a failed assertion cannot leak Chrome.
            try { browser ??= await starting; }
            catch when (browser is null) { }
            if (browser is not null) await browser.DisposeAsync();
        }
    }

    [Test]
    public async Task CallerMutationAfterCaptureDoesNotReplacePatchStrategyCollection()
    {
        var original = new RecordingStrategy();
        var replacement = new RecordingStrategy();
        var strategies = new List<IDriverPatchStrategy> { original };
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var starting = Browser.StartAsync(options with
        {
            Driver = options.Driver with { PatchStrategies = strategies }, PatchMode = DriverPatchMode.ValidateOnly,
            StageHook = async (stage, token) =>
            {
                if (stage != "validation") return;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
        }, deadline.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            strategies.Clear(); strategies.Add(replacement);
            release.TrySetResult(true);
            var error = Assert.ThrowsAsync<BrowserDockException>(async () => await starting);
            Assert.Multiple(() =>
            {
                Assert.That(error!.Category, Is.EqualTo(ErrorCategory.PatchMismatch));
                Assert.That(original.Calls, Is.EqualTo(1));
                Assert.That(replacement.Calls, Is.Zero);
            });
        }
        finally
        {
            deadline.Cancel(); release.TrySetResult(true);
            try { await using var unexpected = await starting; }
            catch (Exception e) when (e is BrowserDockException or OperationCanceledException) { }
        }
    }

    private static async Task<string?> ReadValue(Browser browser, CancellationToken token)
    {
        JsonElement result = await browser.ExecuteCdpAsync("Runtime.evaluate",
            new { expression = "globalThis.__snapshotValue", returnByValue = true }, cancellationToken: token);
        return result.GetProperty("result").GetProperty("value").GetString();
    }

    private sealed class RecordingStrategy : IDriverPatchStrategy
    {
        public int Calls;
        public string RecipeId => "startup-snapshot-probe";
        public IReadOnlyList<PatchPattern> Patterns => Array.Empty<PatchPattern>();
        public bool Supports(Version driverVersion) { Interlocked.Increment(ref Calls); return false; }
    }
}
