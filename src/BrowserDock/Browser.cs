using System.Runtime.InteropServices;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using BrowserDock.Cdp;
using BrowserDock.Hosting;
using BrowserDock.Patching;
using BrowserDock.WebDriver;

namespace BrowserDock;

public sealed class Browser : IAsyncDisposable
{
    private readonly BrowserOptions options;
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly SemaphoreSlim commands = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Admission admission = new();
    private readonly Diagnostics diagnostics;
    private readonly Dictionary<TargetKey, string> windows = new();
    private readonly object stopSync = new();
    private readonly object attachmentSync = new();
    private readonly TimeSpan commandCleanupBudget = TimeSpan.FromSeconds(10);
    private readonly Func<CancellationToken, Task>? commandProbeForTest;
    // A real Attachment with an in-memory W3C executor permits portable command
    // regressions without starting Chrome or reaching its Windows hosting path.
    internal Browser(BrowserOptions options, Attachment testAttachment, Func<CancellationToken, Task> probe, TimeSpan cleanupBudget)
        : this(options)
    {
        commandCleanupBudget = cleanupBudget; commandProbeForTest = probe;
        PublishAttachment(testAttachment);
        OpenAttachment();
    }
    internal SemaphoreSlim CommandsGateForTest => commands;
    internal SemaphoreSlim LifecycleGateForTest => lifecycle;
    internal WebDriverLease LeaseForTest() => NewLease();
    internal void ReplaceAttachmentForTest(Attachment replacement)
    {
        lock (attachmentSync) { _ = admission.Close(); PublishAttachment(replacement); OpenAttachment(); }
    }
    private void PublishAttachment(Attachment? value) { lock (attachmentSync) attachment = value; }
    private void OpenAttachment()
    {
        lock (attachmentSync)
        {
            var current = attachment!;
            var epoch = admission.Open();
            Interlocked.Increment(ref generation);
            current.Executor.IsCurrent = () => AttachmentEpoch == epoch && State == BrowserState.WebDriverAttached;
            SetState(BrowserState.WebDriverAttached);
        }
    }
    private Task? stopTask;
    private Profile? profile;
    private OwnedProcess? chrome;
    private CdpController? cdp;
    private Attachment? attachment;
    private Uri? endpoint;
    private string driverPath = "";
    private long generation;
    private volatile BrowserState state;
    private DateTimeOffset? lastProbe;
    private IReadOnlyList<string> cleanupFailures = [];
    private Browser(BrowserOptions options) { this.options = options; diagnostics = new(options.Logger); }
    public BrowserState State => state;
    public long SessionGeneration => Interlocked.Read(ref generation);
    public long AttachmentEpoch => admission.Epoch;
    internal BrowserOptions EffectiveOptionsForTest => options;
    internal (string? Profile, Uri? Endpoint, string? SessionId, int? DriverPort, long Sent) TestSnapshot => (profile?.Path, endpoint, attachment?.SessionId, attachment?.Port, attachment?.Executor.Sent ?? 0);
    internal void InterruptCdpForTest() => cdp!.InterruptForTest();
    internal bool? StoppedTreeAliveForTest { get; private set; }
    internal Func<(int Pending, int Tasks, int Subscribers, bool SocketOpen)> CaptureResourcesForTest()
    {
        var controller = cdp;
        return () =>
        {
            var value = controller?.ResourcesForTest ?? (Pending: 0, Tasks: 0, Subscribers: 0, SocketOpen: false);
            return (value.Pending, value.Tasks + diagnostics.ActiveWorkersForTest, value.Subscribers, value.SocketOpen);
        };
    }
    private ValueTask StageAsync(string stage, CancellationToken token) => options.StageHook?.Invoke(stage, token) ?? default(ValueTask);
    public BrowserHealthSnapshot Health
    {
        get
        {
            lock (attachmentSync)
            {
                var ownedChrome = chrome; var controller = cdp; var current = attachment;
                return new(State, SessionGeneration, AttachmentEpoch, ownedChrome?.Id, current?.Process.Id,
                    ownedChrome is null ? "NotStarted" : ownedChrome.Alive ? "Running" : "Exited",
                    controller is null ? "Disconnected" : controller.Healthy ? "Connected" : "Failed",
                    current is null ? "None" : current.Process.Alive ? "Attached" : "Failed", lastProbe, cleanupFailures);
            }
        }
    }
    public static async ValueTask<Browser> StartAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        RuntimeCompatibility.NotNull(options, nameof(options));
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000)
            throw new PlatformNotSupportedException("BrowserDock browser hosting requires Windows 11 x64. Common unit/contract tests can run on other platforms.");
        var effectiveOptions = CaptureOptions(options);
        cancellationToken.ThrowIfCancellationRequested();
        var browser = new Browser(effectiveOptions);
        try
        {
            await browser.OperationAsync(async token =>
            {
                browser.SetState(BrowserState.StartingChrome);
                await browser.StageAsync("validation", token).ConfigureAwait(false);
                var chromePath = BrowserHosting.LocateChrome(effectiveOptions.ChromeBinaryPath);
                Version version;
                using (var deadline = new Deadline(effectiveOptions.Timeouts.ChromeStart, token))
                    version = await BrowserHosting.ValidateVersionsAsync(chromePath, effectiveOptions.Driver.ExecutablePath, deadline.Token).ConfigureAwait(false);
                browser.driverPath = await DriverPatchCache.PrepareAsync(effectiveOptions.Driver, version, effectiveOptions.PatchMode, token).ConfigureAwait(false);
                browser.profile = Profile.Acquire(effectiveOptions.Profile);
                await browser.StageAsync("chrome-start", token).ConfigureAwait(false);
                browser.chrome = new OwnedProcess(chromePath, new[] { "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", $"--user-data-dir={browser.profile.Path}", "--no-first-run", "--no-default-browser-check" }.Concat(effectiveOptions.ChromeArguments).Append("about:blank"), tree: true);
                await browser.StageAsync("endpoint", token).ConfigureAwait(false);
                using (var deadline = new Deadline(effectiveOptions.Timeouts.DevToolsEndpointDiscovery, token)) browser.endpoint = await BrowserHosting.DiscoverAsync(browser.profile, browser.chrome, deadline.Token).ConfigureAwait(false);
                browser.cdp = new(browser.endpoint, effectiveOptions.NewDocumentScripts, effectiveOptions.RemoveDiscoveredCdcProperties);
                using (var deadline = new Deadline(effectiveOptions.Timeouts.CdpConnect, token)) await browser.cdp.InitializeAsync(deadline.Token).ConfigureAwait(false);
                await browser.ProbeAsync(token).ConfigureAwait(false);
                browser.SetState(BrowserState.ChromeReady);
                await browser.AttachCoreAsync(false, token).ConfigureAwait(false);
                return true;
            }, effectiveOptions.Timeouts.ChromeStart + effectiveOptions.Timeouts.DevToolsEndpointDiscovery + effectiveOptions.Timeouts.CdpConnect + effectiveOptions.Timeouts.Reconnect, cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch (Exception original)
        {
            try { await browser.StopAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { original.Data["CleanupFailure"] = cleanup; }
            if (original is BrowserDockException error) { error.Diagnostic = browser.Health; error.CleanupFailures = browser.cleanupFailures; }
            throw;
        }
    }
    // Capture and validate the same values before any asynchronous startup work.
    // Strategy objects and the logger remain caller-owned; see their API contracts.
    internal static BrowserOptions CaptureOptions(BrowserOptions options)
    {
        RuntimeCompatibility.NotNull(options, nameof(options));
        RuntimeCompatibility.NotNull(options.Driver, nameof(options.Driver));
        var captured = options with
        {
            ChromeArguments = Array.AsReadOnly(options.ChromeArguments.ToArray()),
            AdditionalUrlSchemes = Array.AsReadOnly(options.AdditionalUrlSchemes.ToArray()),
            NewDocumentScripts = Array.AsReadOnly(options.NewDocumentScripts.ToArray()),
            Driver = options.Driver with { PatchStrategies = Array.AsReadOnly(options.Driver.PatchStrategies.ToArray()) }
        };
        Validate(captured);
        return captured;
    }
    private static void Validate(BrowserOptions options)
    {
        options.Timeouts.Validate();
        if (!Enum.IsDefined(typeof(DriverPatchMode), options.PatchMode) || options.BrowserOwnership != BrowserOwnership.Library) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Invalid ownership or patch mode.");
        foreach (var arg in options.ChromeArguments)
        {
            var name = arg.Split('=')[0];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || new[] { "--user-data-dir", "--profile-directory", "--remote-debugging-port", "--remote-debugging-pipe", "--remote-debugging-address", "--remote-allow-origins", "--headless", "--no-sandbox", "--app", "--proxy-server", "--proxy-pac-url" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new BrowserDockException(ErrorCategory.ConfigurationError, "Chrome argument overrides a managed or unsupported setting.");
        }
    }
    private void SetState(BrowserState next)
    {
        lock (attachmentSync)
        {
            var previous = state; state = next;
            diagnostics.Write(1001, $"State {previous} -> {next}", next, SessionGeneration, AttachmentEpoch);
        }
    }
    private async ValueTask<T> OperationAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken caller, long? expectedEpoch = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
        using var deadline = new Deadline(timeout, linked.Token);
        var id = Guid.NewGuid();
        var acquired = false;
        try
        {
            await lifecycle.WaitAsync(deadline.Token).ConfigureAwait(false); acquired = true;
            RuntimeCompatibility.NotDisposed(lifetime.IsCancellationRequested, this);
            if (expectedEpoch is not null && (AttachmentEpoch != expectedEpoch || State != BrowserState.WebDriverAttached)) throw new StaleAttachmentException();
            diagnostics.Write(1000, "Operation started", State, SessionGeneration, AttachmentEpoch, id);
            return await operation(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!caller.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { throw new BrowserDockException(ErrorCategory.OperationTimedOut, "Browser operation exceeded its deadline.", e) { OperationId = id, Diagnostic = Health, BrowserMayHaveAdvanced = e is NavigationCanceledException { BrowserMayHaveAdvanced: true } }; }
        catch (BrowserDockException e) { e.OperationId = id; e.Diagnostic = Health; throw; }
        finally { if (acquired) lifecycle.Release(); }
    }
    private async Task ProbeAsync(CancellationToken token)
    {
        if (commandProbeForTest is not null) { await commandProbeForTest(token).ConfigureAwait(false); return; }
        if (chrome?.Alive != true) { SetState(BrowserState.Faulted); throw new BrowserExitedException(); }
        if (cdp is null) throw new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "CDP is not initialized.");
        if (!cdp.Healthy)
        {
            try { using var deadline = new Deadline(options.Timeouts.CdpConnect, token); await cdp.RecoverAsync(deadline.Token).ConfigureAwait(false); }
            catch { SetState(BrowserState.Faulted); throw; }
        }
        await cdp.BrowserAsync("Browser.getVersion", null, token).ConfigureAwait(false);
        await cdp.RefreshAsync(token).ConfigureAwait(false);
        lastProbe = DateTimeOffset.UtcNow;
    }
    private async Task AttachCoreAsync(bool reconnect, CancellationToken token)
    {
        await ProbeAsync(token).ConfigureAwait(false);
        if (cdp!.Snapshot().Count > 1 && !cdp.ExplicitSelection) throw new AmbiguousTargetException();
        cdp.Resolve();
        SetState(reconnect ? BrowserState.Reattaching : BrowserState.AttachingWebDriver);
        try
        {
            await StageAsync(reconnect ? "reconnect" : "driver-start", token).ConfigureAwait(false);
            PublishAttachment(await Attachment.CreateAsync(driverPath, endpoint!, options.Timeouts, token).ConfigureAwait(false));
            await StageAsync("session-created", token).ConfigureAwait(false);
            await BindTargetAsync(token).ConfigureAwait(false);
            await ProbeAsync(token).ConfigureAwait(false);
            if (attachment?.Process.Alive != true) throw new BrowserDockException(ErrorCategory.DriverProcessFailure, "Driver exited while binding target.");
            OpenAttachment();
        }
        catch (Exception original)
        {
            _ = admission.Close();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (attachment is not null)
            {
                try { await attachment.DestroyAsync(cleanup.Token).ConfigureAwait(false); PublishAttachment(null); }
                catch (Exception e) { SetState(BrowserState.Faulted); throw new BrowserDockException(ErrorCategory.CleanupIncomplete, "Failed attachment could not be cleaned up.", original) { CleanupFailures = [e.GetType().Name] }; }
            }
            if (chrome?.Alive == true && cdp.Healthy) SetState(BrowserState.CdpOnly); else SetState(BrowserState.Faulted);
            throw;
        }
    }
    private async Task BindTargetAsync(CancellationToken token)
    {
        var target = cdp!.Resolve();
        var marker = "__browserDock_" + Guid.NewGuid().ToString("N");
        var value = Guid.NewGuid().ToString("N");
        await cdp.PageAsync("Runtime.evaluate", new { expression = $"Object.defineProperty(globalThis,{JsonSerializer.Serialize(marker)},{{value:{JsonSerializer.Serialize(value)},configurable:true}})", returnByValue = false }, target.Key, token).ConfigureAwait(false);
        try
        {
            var handle = await attachment!.InvokeAsync(driver =>
            {
                string? found = null;
                foreach (var candidate in driver.WindowHandles)
                {
                    try
                    {
                        driver.SwitchTo().Window(candidate); driver.SwitchTo().DefaultContent();
                        if (driver.ExecuteScript("return globalThis[arguments[0]]", marker)?.ToString() != value) continue;
                        if (found is not null) throw new AmbiguousTargetException();
                        found = candidate;
                    }
                    catch (NoSuchWindowException) { }
                }
                if (found is null) throw new AmbiguousTargetException();
                driver.SwitchTo().Window(found); return found;
            }, token).ConfigureAwait(false);
            lock (windows) windows[target.Key] = handle;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await cdp.PageAsync("Runtime.evaluate", new { expression = $"delete globalThis[{JsonSerializer.Serialize(marker)}]" }, target.Key, cleanup.Token).ConfigureAwait(false); }
            catch { diagnostics.Write(1010, "Temporary target binding marker cleanup failed."); }
        }
    }
    private async Task DisconnectCoreAsync(CancellationToken token)
    {
        if (State == BrowserState.CdpOnly) { await ProbeAsync(token).ConfigureAwait(false); return; }
        if (State != BrowserState.WebDriverAttached) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Disconnect requires an attached WebDriver.");
        var drained = admission.Close();
        SetState(BrowserState.Disconnecting);
        // Once admission is closed, cleanup continues independently of caller cancellation.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await StageAsync("admission-closed", cleanup.Token).ConfigureAwait(false);
            try { await drained.WaitAsync(options.Timeouts.DisconnectDrain, cleanup.Token).ConfigureAwait(false); }
            catch (TimeoutException) { diagnostics.Write(1011, "WebDriver drain timed out; terminating the attachment."); }
            Exception? preparationError = null;
            try
            {
                using var preparation = new Deadline(TimeSpan.FromSeconds(1), cleanup.Token);
                await cdp!.PrepareControlledAsync(preparation.Token).ConfigureAwait(false);
            }
            catch (Exception e) { preparationError = e; diagnostics.Write(1012, "Pre-disconnect script preparation failed; continuing driver cleanup."); }
            if (attachment is not null) { await attachment.DestroyAsync(cleanup.Token).ConfigureAwait(false); PublishAttachment(null); }
            await ProbeAsync(cleanup.Token).ConfigureAwait(false);
            SetState(BrowserState.CdpOnly);
            if (preparationError is not null) throw new BrowserDockException(ErrorCategory.ProtocolError, "Script preparation failed before disconnect; the driver was removed.", preparationError);
        }
        catch { if (State != BrowserState.CdpOnly) SetState(BrowserState.Faulted); throw; }
        token.ThrowIfCancellationRequested();
    }
    public async ValueTask DisconnectWebDriverAsync(CancellationToken cancellationToken = default)
        => await OperationAsync(async token => { await DisconnectCoreAsync(token).ConfigureAwait(false); return true; }, options.Timeouts.DisconnectDrain + TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    public ValueTask EnterCdpOnlyAsync(CancellationToken cancellationToken = default) => DisconnectWebDriverAsync(cancellationToken);
    public ValueTask<WebDriverLease> ReconnectWebDriverAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token =>
        {
            if (State != BrowserState.CdpOnly) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Reconnect requires CdpOnly state.");
            await AttachCoreAsync(true, token).ConfigureAwait(false); return NewLease();
        }, options.Timeouts.Reconnect, cancellationToken);
    private WebDriverLease NewLease()
    {
        lock (attachmentSync)
        {
            if (State != BrowserState.WebDriverAttached || !admission.IsOpen) throw new StaleAttachmentException();
            return new(this, SessionGeneration, AttachmentEpoch);
        }
    }
    public ValueTask<WebDriverLease> GetWebDriverAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token => { await ProbeAsync(token).ConfigureAwait(false); if (attachment is not null && !attachment.Process.Alive) await RemoveFailedAttachmentAsync().ConfigureAwait(false); return NewLease(); }, options.Timeouts.Command, cancellationToken);
    public ValueTask<IReadOnlyList<BrowserTarget>> GetTargetsAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token => { await ProbeAsync(token).ConfigureAwait(false); return cdp!.Snapshot(); }, options.Timeouts.Command, cancellationToken);
    public async ValueTask SelectTargetAsync(TargetKey target, CancellationToken cancellationToken = default)
        => await OperationAsync(async token =>
        {
            await ProbeAsync(token).ConfigureAwait(false);
            await commands.WaitAsync(token).ConfigureAwait(false);
            try { await cdp!.SelectAsync(target, token).ConfigureAwait(false); if (attachment is not null) await BindTargetAsync(token).ConfigureAwait(false); }
            finally { commands.Release(); }
            return true;
        }, options.Timeouts.Command, cancellationToken).ConfigureAwait(false);
    internal async ValueTask SwitchKnownWindowAsync(string handle, long epoch, Func<bool> valid, CancellationToken caller)
        => await OperationAsync(async token =>
        {
            if (!valid()) throw new StaleAttachmentException();
            TargetKey key;
            lock (windows)
            {
                var known = windows.Where(x => x.Value == handle).ToArray();
                if (known.Length != 1) throw new BrowserDockException(ErrorCategory.AmbiguousTarget, "Select the corresponding TargetKey before switching to an unbound window handle.");
                key = known[0].Key;
            }
            await commands.WaitAsync(token).ConfigureAwait(false);
            try { await cdp!.SelectAsync(key, token).ConfigureAwait(false); await BindTargetAsync(token).ConfigureAwait(false); }
            finally { commands.Release(); }
            return true;
        }, options.Timeouts.Command, caller, epoch).ConfigureAwait(false);
    internal void ValidateUrl(Uri url)
    {
        RuntimeCompatibility.NotNull(url, nameof(url));
        if (!url.IsAbsoluteUri || !new[] { "http", "https", "about", "data" }.Concat(options.AdditionalUrlSchemes).Contains(url.Scheme, StringComparer.OrdinalIgnoreCase))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "URL scheme is not enabled.");
    }
    // Portable preflight fixture: no browser, driver, profile or CDP connection.
    // StageHook can stop accepted requests immediately before the health probe.
    internal static Browser NavigationFixtureForTest(BrowserOptions options, BrowserState initialState)
    {
        var browser = new Browser(CaptureOptions(options)) { state = initialState };
        if (initialState == BrowserState.WebDriverAttached)
        {
            _ = browser.admission.Open();
            browser.generation = 1;
        }
        return browser;
    }
    internal static void ValidateNavigation(BrowserState state, NavigationOptions options, TimeSpan navigationBudget)
    {
        if (!Enum.IsDefined(typeof(NavigationMode), options.Mode) || !Enum.IsDefined(typeof(NavigationWaitUntil), options.WaitUntil) || !Enum.IsDefined(typeof(TargetPolicy), options.TargetPolicy))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "Invalid navigation options.");
        if (options.ReconnectAfterNavigation && options.Mode != NavigationMode.Detached)
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "ReconnectAfterNavigation requires Detached navigation.");
        if (options.ReconnectDelay is { } delay)
        {
            if (options.Mode != NavigationMode.Detached || !options.ReconnectAfterNavigation)
                throw new BrowserDockException(ErrorCategory.ConfigurationError, "ReconnectDelay requires Detached navigation with ReconnectAfterNavigation.");
            if (delay < TimeSpan.Zero || delay >= navigationBudget)
                throw new BrowserDockException(ErrorCategory.ConfigurationError, "ReconnectDelay must be nonnegative and less than the Navigation timeout.");
        }
        if ((options.Mode == NavigationMode.Standard && (state != BrowserState.WebDriverAttached || options.TargetPolicy != TargetPolicy.CurrentControlled)) ||
            (options.Mode == NavigationMode.Detached && state is not (BrowserState.WebDriverAttached or BrowserState.CdpOnly)) ||
            (options.Mode == NavigationMode.CdpOnly && state is not (BrowserState.CdpOnly or BrowserState.ChromeReady)))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "Navigation options contradict the current browser state.");
    }
    public ValueTask<NavigationResult> NavigateAsync(Uri url, NavigationOptions? options = null, CancellationToken cancellationToken = default)
        => NavigateInternalAsync(url, options ?? new(), null, cancellationToken);
    internal ValueTask<NavigationResult> NavigateFromLeaseAsync(Uri url, long epoch, Func<bool> valid, CancellationToken token) => NavigateInternalAsync(url, new(), epoch, token, valid);
    private ValueTask<NavigationResult> NavigateInternalAsync(Uri url, NavigationOptions navigation, long? epoch, CancellationToken caller, Func<bool>? valid = null)
    {
        ValidateUrl(url);
        return OperationAsync(async token =>
        {
            if (valid?.Invoke() == false) throw new StaleAttachmentException();
            ValidateNavigation(State, navigation, options.Timeouts.Navigation);
            await StageAsync("navigation-probe", token).ConfigureAwait(false);
            await ProbeAsync(token).ConfigureAwait(false);
            if (navigation.Target is not null) await cdp!.SelectAsync(navigation.Target.Value, token).ConfigureAwait(false);
            if (navigation.Mode == NavigationMode.Detached && navigation.ReconnectAfterNavigation && cdp!.Snapshot().Count > 1 && !cdp.ExplicitSelection) throw new AmbiguousTargetException();
            if (navigation.Mode == NavigationMode.Detached && State == BrowserState.WebDriverAttached) await DisconnectCoreAsync(token).ConfigureAwait(false);
            var advanced = false;
            try
            {
                await StageAsync("navigation", token).ConfigureAwait(false);
                if (navigation.TargetPolicy == TargetPolicy.ReplaceControlled) await cdp!.ReplaceAsync(token).ConfigureAwait(false);
                var selected = navigation with { Target = cdp!.Controlled };
                PageResult result;
                if (navigation.Mode == NavigationMode.Standard)
                {
                    await commands.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await BindTargetAsync(token).ConfigureAwait(false);
                        advanced = true;
                        using var admitted = admission.Enter(AttachmentEpoch);
                        result = await cdp.NavigateAsync(url, selected, async ct => await attachment!.InvokeAsync(d => { d.Navigate().GoToUrl(url); return true; }, ct).ConfigureAwait(false), token).ConfigureAwait(false);
                    }
                    finally { commands.Release(); }
                }
                else { advanced = true; result = await cdp.NavigateAsync(url, selected, null, token).ConfigureAwait(false); SetState(BrowserState.CdpOnly); }
                if (navigation.Mode == NavigationMode.Detached && navigation.ReconnectAfterNavigation)
                {
                    if (navigation.ReconnectDelay is { } delay) await Task.Delay(delay, token).ConfigureAwait(false);
                    using var deadline = new Deadline(this.options.Timeouts.Reconnect, token);
                    await AttachCoreAsync(true, deadline.Token).ConfigureAwait(false);
                }
                return new NavigationResult(result.Url, result.Redirects, result.Outcome, SessionGeneration, AttachmentEpoch);
            }
            catch (Exception e)
            {
                if (attachment is not null && (!attachment.Process.Alive || e is WebDriverException or OperationCanceledException)) await RemoveFailedAttachmentAsync().ConfigureAwait(false);
                if (chrome?.Alive != true) SetState(BrowserState.Faulted);
                if (e is OperationCanceledException) throw new NavigationCanceledException(caller, advanced);
                var error = Attachment.Map(e); error.BrowserMayHaveAdvanced = advanced; throw error;
            }
        }, options.Timeouts.Navigation, caller, epoch);
    }
    public ValueTask<JsonElement> ExecuteCdpAsync(string method, object? parameters = null, TargetKey? target = null, CancellationToken cancellationToken = default)
        => OperationAsync(async token =>
        {
            await ProbeAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(method) || !method.Contains('.')) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Use a domain-qualified CDP method.");
            if (method is "Page.navigate" or "Target.createTarget")
            {
                var json = JsonSerializer.SerializeToElement(parameters);
                if (json.TryGetProperty("url", out var url)) ValidateUrl(new Uri(url.GetString()!, UriKind.Absolute));
            }
            diagnostics.Write(1020, $"Advanced CDP method: {method}");
            if (method.StartsWith("Browser.", StringComparison.Ordinal) || method.StartsWith("Target.", StringComparison.Ordinal))
            {
                if (target is not null) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Browser/Target commands cannot have a page target.");
                return await cdp!.BrowserAsync(method, parameters, token).ConfigureAwait(false);
            }
            return await cdp!.PageAsync(method, parameters, target, token).ConfigureAwait(false);
        }, options.Timeouts.Command, cancellationToken);
    internal async ValueTask<T> CommandAsync<T>(long epoch, Func<bool> valid, Func<RemoteWebDriver, T> action, CancellationToken caller)
    {
        var id = Guid.NewGuid();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
        using var deadline = new Deadline(options.Timeouts.Command, linked.Token);
        var acquired = false;
        var execution = new Attachment.CommandExecution();
        Attachment? used = null;
        long? closedEpoch = null;
        Exception failure;
        Exception error;
        var cleanupErrors = new List<string>();
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!valid() || State != BrowserState.WebDriverAttached || epoch != AttachmentEpoch) throw new StaleAttachmentException();
            await commands.WaitAsync(deadline.Token).ConfigureAwait(false); acquired = true;
            deadline.Token.ThrowIfCancellationRequested();
            if (!valid() || State != BrowserState.WebDriverAttached || epoch != AttachmentEpoch) throw new StaleAttachmentException();
            IDisposable entered;
            lock (attachmentSync)
            {
                used = attachment ?? throw new StaleAttachmentException();
                entered = admission.Enter(epoch);
            }
            using (entered)
            {
                await StageAsync("command-dispatch", deadline.Token).ConfigureAwait(false);
                diagnostics.Write(1000, "WebDriver command started", State, SessionGeneration, epoch, id);
                return await used.InvokeAsync(action, deadline.Token, execution).ConfigureAwait(false);
            }
        }
        catch (Exception original)
        {
            failure = original;
            // Freeze the cancellation reason before independent recovery begins.
            // If both are already signaled, explicit caller cancellation wins.
            if (original is OperationCanceledException)
            {
                if (caller.IsCancellationRequested)
                    error = new OperationCanceledException("WebDriver command was canceled by its caller.", original, caller);
                else if (lifetime.IsCancellationRequested)
                    error = new OperationCanceledException("Browser lifetime ended during the WebDriver command.", original, lifetime.Token);
                else
                    error = new BrowserDockException(ErrorCategory.OperationTimedOut, "WebDriver command exceeded its deadline.", original);
            }
            else error = Attachment.Map(original);
            if (execution.Started && used is not null && (original is OperationCanceledException ||
                error is BrowserDockException { Category: ErrorCategory.AttachmentLost or ErrorCategory.OperationTimedOut }))
            {
                used.Executor.Detach();
                lock (attachmentSync)
                {
                    if (ReferenceEquals(attachment, used) && admission.TryClose(epoch, out var closed)) closedEpoch = closed;
                }
            }
        }
        finally { if (acquired) commands.Release(); }

        if (execution.Started && used is not null && (failure is OperationCanceledException ||
            error is BrowserDockException { Category: ErrorCategory.AttachmentLost or ErrorCategory.OperationTimedOut }))
            await RecoverCommandAsync(used, closedEpoch, cleanupErrors).ConfigureAwait(false);
        if (failure.Data["DriverCleanupFailure"] is Exception ioFailure) cleanupErrors.Add($"driver I/O: {ioFailure.GetType().Name}");
        if (error is BrowserDockException browserError)
        {
            browserError.OperationId = id; browserError.Diagnostic = Health;
            browserError.CleanupFailures = browserError.CleanupFailures.Concat(cleanupErrors).ToArray();
        }
        else
        {
            error.Data["OperationId"] = id; error.Data["Diagnostic"] = Health;
            error.Data["CleanupFailures"] = cleanupErrors.ToArray();
        }
        throw error;
    }
    private async Task RecoverCommandAsync(Attachment failed, long? closedEpoch, List<string> failures)
    {
        using var cleanup = new CancellationTokenSource(commandCleanupBudget);
        var held = false;
        // Cleanup owns the captured attachment, never whatever is current later.
        var destroying = failed.DestroyAsync(cleanup.Token);
        try
        {
            await StageAsync("command-recovery", cleanup.Token).ConfigureAwait(false);
            await lifecycle.WaitAsync(cleanup.Token).ConfigureAwait(false); held = true;
            await destroying.ConfigureAwait(false);
            lock (attachmentSync)
            {
                if (!ReferenceEquals(attachment, failed) || closedEpoch != AttachmentEpoch ||
                    lifetime.IsCancellationRequested || State != BrowserState.WebDriverAttached) return;
                PublishAttachment(null);
            }
            await ProbeAsync(cleanup.Token).ConfigureAwait(false);
            lock (attachmentSync)
            {
                if (closedEpoch == AttachmentEpoch && attachment is null && !lifetime.IsCancellationRequested &&
                    State == BrowserState.WebDriverAttached) SetState(BrowserState.CdpOnly);
            }
        }
        catch (Exception e)
        {
            failures.Add($"{(held ? "driver/probe" : "lifecycle")}: {e.GetType().Name}");
        }
        finally
        {
            // Observe an independently failing disposal even when gate waiting expired.
            try { await destroying.ConfigureAwait(false); }
            catch (Exception e) { failures.Add($"driver: {e.GetType().Name}"); }
            lock (attachmentSync)
            {
                if (failures.Count != 0 && closedEpoch == AttachmentEpoch && !lifetime.IsCancellationRequested &&
                    (State is BrowserState.WebDriverAttached or BrowserState.Faulted) &&
                    (attachment is null || ReferenceEquals(attachment, failed)))
                {
                    cleanupFailures = failures.ToArray(); SetState(BrowserState.Faulted);
                }
            }
            if (held) lifecycle.Release();
        }
    }
    private async Task RemoveFailedAttachmentAsync()
    {
        _ = admission.Close();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (attachment is not null) { await attachment.DestroyAsync(cleanup.Token).ConfigureAwait(false); PublishAttachment(null); }
            await ProbeAsync(cleanup.Token).ConfigureAwait(false); SetState(BrowserState.CdpOnly);
        }
        catch (Exception e) { cleanupFailures = [e.GetType().Name]; SetState(BrowserState.Faulted); }
    }
    public ValueTask<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default)
        => FindForLeaseAsync(locator, options ?? new(), AttachmentEpoch, () => true, cancellationToken);
    internal ValueTask<ElementRef> FindForLeaseAsync(Locator locator, FindOptions find, long epoch, Func<bool> valid, CancellationToken token)
    {
        RuntimeCompatibility.NotNull(locator, nameof(locator));
        var copy = find with { FramePath = find.FramePath.ToArray() };
        return CommandAsync(epoch, valid, driver =>
        {
            var target = cdp!.Resolve(copy.Target).Key;
            RestoreElementContext(driver, target, copy.FramePath);
            _ = driver.FindElement(Attachment.By(locator));
            var id = attachment!.Executor.LastFoundElementId ?? throw new BrowserDockException(ErrorCategory.ProtocolError, "W3C find-element response did not contain an element reference.");
            return new ElementRef(this, id, locator, copy, target, SessionGeneration, epoch);
        }, token);
    }
    internal void RestoreElementContext(RemoteWebDriver driver, TargetKey target, IReadOnlyList<Locator> framePath)
    {
        cdp!.Resolve(target);
        string? handle;
        lock (windows) windows.TryGetValue(target, out handle);
        if (handle is null) throw new BrowserDockException(ErrorCategory.AmbiguousTarget, "Select the target before locating elements in it.");
        driver.SwitchTo().Window(handle); driver.SwitchTo().DefaultContent();
        foreach (var frame in framePath) driver.SwitchTo().Frame(driver.FindElement(Attachment.By(frame)));
    }
    public ValueTask StopAsync(ShutdownOptions? options = null, CancellationToken cancellationToken = default)
    {
        lock (stopSync)
        {
            lifetime.Cancel();
            stopTask ??= StopCoreAsync();
            return new(stopTask);
        }
    }
    private async Task StopCoreAsync()
    {
        var failures = new List<string>();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var held = false;
        try
        {
            await lifecycle.WaitAsync(cleanup.Token).ConfigureAwait(false); held = true;
            SetState(BrowserState.Disposing);
            try { await StageAsync("cleanup", cleanup.Token).ConfigureAwait(false); }
            catch (Exception e) { failures.Add($"cleanup observer: {e.GetType().Name}"); }
            var drained = admission.Close();
            async Task Step(string name, Func<CancellationToken, Task> action, TimeSpan? limit = null)
            {
                try { using var deadline = new Deadline(limit ?? TimeSpan.FromSeconds(10), cleanup.Token); await action(deadline.Token).ConfigureAwait(false); }
                catch (Exception e) { failures.Add($"{name}: {e.GetType().Name}"); }
            }
            await Step("command drain", ct => drained.WaitAsync(ct), this.options.Timeouts.DisconnectDrain).ConfigureAwait(false);
            if (attachment is not null)
            {
                await Step("driver", async ct => { await attachment.DestroyAsync(ct).ConfigureAwait(false); PublishAttachment(null); }, this.options.Timeouts.ForceKillWait).ConfigureAwait(false);
            }
            if (chrome is not null)
            {
                if (chrome.Alive && cdp?.Healthy == true)
                {
                    try
                    {
                        using var graceful = new Deadline(this.options.Timeouts.GracefulShutdown, cleanup.Token);
                        try { await cdp.BrowserAsync("Browser.close", null, graceful.Token).ConfigureAwait(false); } catch (BrowserDockException) { /* Browser.close may close the socket before replying. */ }
                        await chrome.WaitAsync(graceful.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { diagnostics.Write(1030, "Graceful Chrome shutdown timed out; terminating the owned job."); }
                }
                await Step("chrome", async ct => { chrome.Terminate(); await chrome.WaitAsync(ct).ConfigureAwait(false); }, this.options.Timeouts.ForceKillWait).ConfigureAwait(false);
            }
            if (cdp is not null) await Step("cdp", async ct => { await cdp.DisposeAsync().AsTask().WaitAsync(ct).ConfigureAwait(false); cdp = null; }).ConfigureAwait(false);
            if (profile is not null) await Step("profile", async ct => { await profile.CleanupAsync(chrome?.TreeAlive != true, ct).ConfigureAwait(false); profile = null; }, this.options.Timeouts.ProfileCleanup).ConfigureAwait(false);
            if (chrome is not null)
            {
                StoppedTreeAliveForTest = chrome.TreeAlive;
                if (!chrome.TreeAlive) { chrome.Dispose(); chrome = null; }
            }
            await Step("diagnostics", async ct => await diagnostics.DisposeAsync().AsTask().WaitAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception e) { failures.Add($"cleanup deadline: {e.GetType().Name}"); }
        finally
        {
            lock (attachmentSync)
            {
                cleanupFailures = failures.ToArray();
                state = failures.Count == 0 ? BrowserState.Stopped : BrowserState.Faulted;
            }
            if (held) lifecycle.Release();
        }
        if (failures.Count > 0) throw new BrowserDockException(ErrorCategory.CleanupIncomplete, "Browser cleanup was incomplete.") { Diagnostic = Health, CleanupFailures = cleanupFailures };
    }
    public ValueTask DisposeAsync() => StopAsync();
}
