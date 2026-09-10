using System.Runtime.InteropServices;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using UcDotNet.Cdp;
using UcDotNet.Hosting;
using UcDotNet.Patching;
using UcDotNet.WebDriver;

namespace UcDotNet;

public sealed class UcBrowser : IAsyncDisposable
{
    private readonly BrowserOptions options;
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly SemaphoreSlim commands = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Admission admission = new();
    private readonly Diagnostics diagnostics;
    private readonly Dictionary<TargetKey, string> windows = new();
    private readonly object stopSync = new();
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
    private UcBrowser(BrowserOptions options) { this.options = options; diagnostics = new(options.Logger); }
    public BrowserState State => state;
    public long SessionGeneration => Interlocked.Read(ref generation);
    public long AttachmentEpoch => admission.Epoch;
    internal (string? Profile, Uri? Endpoint, string? SessionId, int? DriverPort, long Sent) TestSnapshot => (profile?.Path, endpoint, attachment?.SessionId, attachment?.Port, attachment?.Executor.Sent ?? 0);
    internal void InterruptCdpForTest() => cdp!.InterruptForTest();
    private ValueTask StageAsync(string stage, CancellationToken token) => options.StageHook?.Invoke(stage, token) ?? ValueTask.CompletedTask;
    public BrowserHealthSnapshot Health => new(State, SessionGeneration, AttachmentEpoch, chrome?.Id, attachment?.Process.Id,
        chrome is null ? "NotStarted" : chrome.Alive ? "Running" : "Exited", cdp is null ? "Disconnected" : cdp.Healthy ? "Connected" : "Failed",
        attachment is null ? "None" : attachment.Process.Alive ? "Attached" : "Failed", lastProbe, cleanupFailures);
    public static async ValueTask<UcBrowser> StartAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        cancellationToken.ThrowIfCancellationRequested();
        var browser = new UcBrowser(options with
        {
            ChromeArguments = options.ChromeArguments.ToArray(), AdditionalUrlSchemes = options.AdditionalUrlSchemes.ToArray(), NewDocumentScripts = options.NewDocumentScripts.ToArray(),
            Driver = options.Driver with { PatchStrategies = options.Driver.PatchStrategies.ToArray() }
        });
        try
        {
            await browser.OperationAsync(async token =>
            {
                browser.SetState(BrowserState.StartingChrome);
                await browser.StageAsync("validation", token);
                var chromePath = BrowserHosting.LocateChrome(options.ChromeBinaryPath);
                Version version;
                using (var deadline = new Deadline(options.Timeouts.ChromeStart, token))
                    version = await BrowserHosting.ValidateVersionsAsync(chromePath, options.Driver.ExecutablePath, deadline.Token);
                browser.driverPath = await DriverPatchCache.PrepareAsync(options.Driver, version, options.PatchMode, token);
                browser.profile = Profile.Acquire(options.Profile);
                await browser.StageAsync("chrome-start", token);
                browser.chrome = new OwnedProcess(chromePath, new[] { "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", $"--user-data-dir={browser.profile.Path}", "--no-first-run", "--no-default-browser-check" }.Concat(options.ChromeArguments).Append("about:blank"), tree: true);
                await browser.StageAsync("endpoint", token);
                using (var deadline = new Deadline(options.Timeouts.DevToolsEndpointDiscovery, token)) browser.endpoint = await BrowserHosting.DiscoverAsync(browser.profile, browser.chrome, deadline.Token);
                browser.cdp = new(browser.endpoint, options.NewDocumentScripts, options.RemoveDiscoveredCdcProperties);
                using (var deadline = new Deadline(options.Timeouts.CdpConnect, token)) await browser.cdp.InitializeAsync(deadline.Token);
                await browser.ProbeAsync(token);
                browser.SetState(BrowserState.ChromeReady);
                await browser.AttachCoreAsync(false, token);
                return true;
            }, options.Timeouts.ChromeStart + options.Timeouts.DevToolsEndpointDiscovery + options.Timeouts.CdpConnect + options.Timeouts.Reconnect, cancellationToken);
            return browser;
        }
        catch (Exception original)
        {
            try { await browser.StopAsync(); }
            catch (Exception cleanup) { original.Data["CleanupFailure"] = cleanup; }
            if (original is UcException uc) { uc.Diagnostic = browser.Health; uc.CleanupFailures = browser.cleanupFailures; }
            throw;
        }
    }
    private static void Validate(BrowserOptions options)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || Environment.OSVersion.Version.Build < 22000)
            throw new PlatformNotSupportedException("UcDotNet browser hosting requires Windows 11 x64. Common unit/contract tests can run on other platforms.");
        ArgumentNullException.ThrowIfNull(options.Driver);
        options.Timeouts.Validate();
        if (!Enum.IsDefined(options.PatchMode) || options.BrowserOwnership != BrowserOwnership.Library) throw new UcException(ErrorCategory.ConfigurationError, "Invalid ownership or patch mode.");
        foreach (var arg in options.ChromeArguments)
        {
            var name = arg.Split('=')[0];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || new[] { "--user-data-dir", "--profile-directory", "--remote-debugging-port", "--remote-debugging-pipe", "--remote-debugging-address", "--remote-allow-origins", "--headless", "--no-sandbox", "--app", "--proxy-server", "--proxy-pac-url" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new UcException(ErrorCategory.ConfigurationError, "Chrome argument overrides a managed or unsupported setting.");
        }
    }
    private void SetState(BrowserState next)
    {
        var previous = state; state = next;
        diagnostics.Write(1001, $"State {previous} -> {next}", next, SessionGeneration, AttachmentEpoch);
    }
    private async ValueTask<T> OperationAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken caller, long? expectedEpoch = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
        using var deadline = new Deadline(timeout, linked.Token);
        var id = Guid.NewGuid();
        var acquired = false;
        try
        {
            await lifecycle.WaitAsync(deadline.Token); acquired = true;
            ObjectDisposedException.ThrowIf(lifetime.IsCancellationRequested, this);
            if (expectedEpoch is not null && (AttachmentEpoch != expectedEpoch || State != BrowserState.WebDriverAttached)) throw new StaleAttachmentException();
            diagnostics.Write(1000, "Operation started", State, SessionGeneration, AttachmentEpoch, id);
            return await operation(deadline.Token);
        }
        catch (OperationCanceledException e) when (!caller.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { throw new UcException(ErrorCategory.OperationTimedOut, "Browser operation exceeded its deadline.", e) { OperationId = id, Diagnostic = Health, BrowserMayHaveAdvanced = e is NavigationCanceledException { BrowserMayHaveAdvanced: true } }; }
        catch (UcException e) { e.OperationId = id; e.Diagnostic = Health; throw; }
        finally { if (acquired) lifecycle.Release(); }
    }
    private async Task ProbeAsync(CancellationToken token)
    {
        if (chrome?.Alive != true) { SetState(BrowserState.Faulted); throw new BrowserExitedException(); }
        if (cdp is null) throw new UcException(ErrorCategory.DevToolsEndpointFailure, "CDP is not initialized.");
        if (!cdp.Healthy)
        {
            try { using var deadline = new Deadline(options.Timeouts.CdpConnect, token); await cdp.RecoverAsync(deadline.Token); }
            catch { SetState(BrowserState.Faulted); throw; }
        }
        await cdp.BrowserAsync("Browser.getVersion", null, token);
        await cdp.RefreshAsync(token);
        lastProbe = DateTimeOffset.UtcNow;
    }
    private async Task AttachCoreAsync(bool reconnect, CancellationToken token)
    {
        await ProbeAsync(token);
        if (cdp!.Snapshot().Count > 1 && !cdp.ExplicitSelection) throw new AmbiguousTargetException();
        cdp.Resolve();
        SetState(reconnect ? BrowserState.Reattaching : BrowserState.AttachingWebDriver);
        try
        {
            await StageAsync(reconnect ? "reconnect" : "driver-start", token);
            attachment = await Attachment.CreateAsync(driverPath, endpoint!, options.Timeouts, token);
            await StageAsync("session-created", token);
            await BindTargetAsync(token);
            await ProbeAsync(token);
            if (!attachment.Process.Alive) throw new UcException(ErrorCategory.DriverProcessFailure, "Driver exited while binding target.");
            var epoch = admission.Open();
            Interlocked.Increment(ref generation);
            attachment.Executor.IsCurrent = () => AttachmentEpoch == epoch && State == BrowserState.WebDriverAttached;
            SetState(BrowserState.WebDriverAttached);
        }
        catch (Exception original)
        {
            _ = admission.Close();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (attachment is not null)
            {
                try { await attachment.DestroyAsync(cleanup.Token); attachment = null; }
                catch (Exception e) { SetState(BrowserState.Faulted); throw new UcException(ErrorCategory.CleanupIncomplete, "Failed attachment could not be cleaned up.", original) { CleanupFailures = [e.GetType().Name] }; }
            }
            if (chrome?.Alive == true && cdp.Healthy) SetState(BrowserState.CdpOnly); else SetState(BrowserState.Faulted);
            throw;
        }
    }
    private async Task BindTargetAsync(CancellationToken token)
    {
        var target = cdp!.Resolve();
        var marker = "__ucdotnet_" + Guid.NewGuid().ToString("N");
        var value = Guid.NewGuid().ToString("N");
        await cdp.PageAsync("Runtime.evaluate", new { expression = $"Object.defineProperty(globalThis,{JsonSerializer.Serialize(marker)},{{value:{JsonSerializer.Serialize(value)},configurable:true}})", returnByValue = false }, target.Key, token);
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
            }, token);
            lock (windows) windows[target.Key] = handle;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await cdp.PageAsync("Runtime.evaluate", new { expression = $"delete globalThis[{JsonSerializer.Serialize(marker)}]" }, target.Key, cleanup.Token); }
            catch { diagnostics.Write(1010, "Temporary target binding marker cleanup failed."); }
        }
    }
    private async Task DisconnectCoreAsync(CancellationToken token)
    {
        if (State == BrowserState.CdpOnly) { await ProbeAsync(token); return; }
        if (State != BrowserState.WebDriverAttached) throw new UcException(ErrorCategory.ConfigurationError, "Disconnect requires an attached WebDriver.");
        var drained = admission.Close();
        SetState(BrowserState.Disconnecting);
        // Once admission is closed, cleanup continues independently of caller cancellation.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            try { await drained.WaitAsync(options.Timeouts.DisconnectDrain, cleanup.Token); }
            catch (TimeoutException) { diagnostics.Write(1011, "WebDriver drain timed out; terminating the attachment."); }
            Exception? preparationError = null;
            try
            {
                using var preparation = new Deadline(TimeSpan.FromSeconds(1), cleanup.Token);
                await cdp!.PrepareControlledAsync(preparation.Token);
            }
            catch (Exception e) { preparationError = e; diagnostics.Write(1012, "Pre-disconnect script preparation failed; continuing driver cleanup."); }
            if (attachment is not null) { await attachment.DestroyAsync(cleanup.Token); attachment = null; }
            await ProbeAsync(cleanup.Token);
            SetState(BrowserState.CdpOnly);
            if (preparationError is not null) throw new UcException(ErrorCategory.ProtocolError, "Script preparation failed before disconnect; the driver was removed.", preparationError);
        }
        catch { if (State != BrowserState.CdpOnly) SetState(BrowserState.Faulted); throw; }
        token.ThrowIfCancellationRequested();
    }
    public async ValueTask DisconnectWebDriverAsync(CancellationToken cancellationToken = default)
        => await OperationAsync(async token => { await DisconnectCoreAsync(token); return true; }, options.Timeouts.DisconnectDrain + TimeSpan.FromSeconds(10), cancellationToken);
    public ValueTask EnterCdpOnlyAsync(CancellationToken cancellationToken = default) => DisconnectWebDriverAsync(cancellationToken);
    public ValueTask<WebDriverLease> ReconnectWebDriverAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token =>
        {
            if (State != BrowserState.CdpOnly) throw new UcException(ErrorCategory.ConfigurationError, "Reconnect requires CdpOnly state.");
            await AttachCoreAsync(true, token); return NewLease();
        }, options.Timeouts.Reconnect, cancellationToken);
    private WebDriverLease NewLease()
    {
        if (State != BrowserState.WebDriverAttached) throw new StaleAttachmentException();
        return new(this, SessionGeneration, AttachmentEpoch);
    }
    public ValueTask<WebDriverLease> GetWebDriverAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token => { await ProbeAsync(token); if (attachment is not null && !attachment.Process.Alive) await RemoveFailedAttachmentAsync(); return NewLease(); }, options.Timeouts.Command, cancellationToken);
    public ValueTask<IReadOnlyList<BrowserTarget>> GetTargetsAsync(CancellationToken cancellationToken = default)
        => OperationAsync(async token => { await ProbeAsync(token); return cdp!.Snapshot(); }, options.Timeouts.Command, cancellationToken);
    public async ValueTask SelectTargetAsync(TargetKey target, CancellationToken cancellationToken = default)
        => await OperationAsync(async token =>
        {
            await ProbeAsync(token);
            await commands.WaitAsync(token);
            try { await cdp!.SelectAsync(target, token); if (attachment is not null) await BindTargetAsync(token); }
            finally { commands.Release(); }
            return true;
        }, options.Timeouts.Command, cancellationToken);
    internal async ValueTask SwitchKnownWindowAsync(string handle, long epoch, Func<bool> valid, CancellationToken caller)
        => await OperationAsync(async token =>
        {
            if (!valid()) throw new StaleAttachmentException();
            TargetKey key;
            lock (windows)
            {
                var known = windows.Where(x => x.Value == handle).ToArray();
                if (known.Length != 1) throw new UcException(ErrorCategory.AmbiguousTarget, "Select the corresponding TargetKey before switching to an unbound window handle.");
                key = known[0].Key;
            }
            await commands.WaitAsync(token);
            try { await cdp!.SelectAsync(key, token); await BindTargetAsync(token); }
            finally { commands.Release(); }
            return true;
        }, options.Timeouts.Command, caller, epoch);
    internal void ValidateUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || !new[] { "http", "https", "about", "data" }.Concat(options.AdditionalUrlSchemes).Contains(url.Scheme, StringComparer.OrdinalIgnoreCase))
            throw new UcException(ErrorCategory.ConfigurationError, "URL scheme is not enabled.");
    }
    internal static void ValidateNavigation(BrowserState state, NavigationOptions options)
    {
        if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.WaitUntil) || !Enum.IsDefined(options.TargetPolicy) || options.ReconnectDelay < TimeSpan.Zero)
            throw new UcException(ErrorCategory.ConfigurationError, "Invalid navigation options.");
        if ((options.Mode == NavigationMode.Standard && (state != BrowserState.WebDriverAttached || options.TargetPolicy != TargetPolicy.CurrentControlled)) ||
            (options.Mode == NavigationMode.Detached && state is not (BrowserState.WebDriverAttached or BrowserState.CdpOnly)) ||
            (options.Mode == NavigationMode.CdpOnly && (state is not (BrowserState.CdpOnly or BrowserState.ChromeReady) || options.ReconnectAfterNavigation)) ||
            (options.Mode != NavigationMode.Detached && options.ReconnectDelay is not null))
            throw new UcException(ErrorCategory.ConfigurationError, "Navigation options contradict the current browser state.");
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
            ValidateNavigation(State, navigation);
            await ProbeAsync(token);
            if (navigation.Target is not null) await cdp!.SelectAsync(navigation.Target.Value, token);
            if (navigation.Mode == NavigationMode.Detached && navigation.ReconnectAfterNavigation && cdp!.Snapshot().Count > 1 && !cdp.ExplicitSelection) throw new AmbiguousTargetException();
            if (navigation.Mode == NavigationMode.Detached && State == BrowserState.WebDriverAttached) await DisconnectCoreAsync(token);
            var advanced = false;
            try
            {
                await StageAsync("navigation", token);
                if (navigation.TargetPolicy == TargetPolicy.ReplaceControlled) await cdp!.ReplaceAsync(token);
                var selected = navigation with { Target = cdp!.Controlled };
                PageResult result;
                if (navigation.Mode == NavigationMode.Standard)
                {
                    await commands.WaitAsync(token);
                    try
                    {
                        await BindTargetAsync(token);
                        advanced = true;
                        using var admitted = admission.Enter(AttachmentEpoch);
                        result = await cdp.NavigateAsync(url, selected, async ct => await attachment!.InvokeAsync(d => { d.Navigate().GoToUrl(url); return true; }, ct), token);
                    }
                    finally { commands.Release(); }
                }
                else { advanced = true; result = await cdp.NavigateAsync(url, selected, null, token); SetState(BrowserState.CdpOnly); }
                if (navigation.Mode == NavigationMode.Detached && navigation.ReconnectAfterNavigation)
                {
                    if (navigation.ReconnectDelay is { } delay) await Task.Delay(delay, token);
                    using var deadline = new Deadline(this.options.Timeouts.Reconnect, token);
                    await AttachCoreAsync(true, deadline.Token);
                }
                return new NavigationResult(result.Url, result.Redirects, result.Outcome, SessionGeneration, AttachmentEpoch);
            }
            catch (Exception e)
            {
                if (attachment is not null && (!attachment.Process.Alive || e is WebDriverException or OperationCanceledException)) await RemoveFailedAttachmentAsync();
                if (chrome?.Alive != true) SetState(BrowserState.Faulted);
                if (e is OperationCanceledException) throw new NavigationCanceledException(caller, advanced);
                var error = Attachment.Map(e); error.BrowserMayHaveAdvanced = advanced; throw error;
            }
        }, options.Timeouts.Navigation, caller, epoch);
    }
    public ValueTask<JsonElement> ExecuteCdpAsync(string method, object? parameters = null, TargetKey? target = null, CancellationToken cancellationToken = default)
        => OperationAsync(async token =>
        {
            await ProbeAsync(token);
            if (string.IsNullOrWhiteSpace(method) || !method.Contains('.')) throw new UcException(ErrorCategory.ConfigurationError, "Use a domain-qualified CDP method.");
            if (method is "Page.navigate" or "Target.createTarget")
            {
                var json = JsonSerializer.SerializeToElement(parameters);
                if (json.TryGetProperty("url", out var url)) ValidateUrl(new Uri(url.GetString()!, UriKind.Absolute));
            }
            diagnostics.Write(1020, $"Advanced CDP method: {method}");
            if (method.StartsWith("Browser.", StringComparison.Ordinal) || method.StartsWith("Target.", StringComparison.Ordinal))
            {
                if (target is not null) throw new UcException(ErrorCategory.ConfigurationError, "Browser/Target commands cannot have a page target.");
                return await cdp!.BrowserAsync(method, parameters, token);
            }
            return await cdp!.PageAsync(method, parameters, target, token);
        }, options.Timeouts.Command, cancellationToken);
    internal async ValueTask<T> CommandAsync<T>(long epoch, Func<bool> valid, Func<RemoteWebDriver, T> action, CancellationToken caller)
    {
        if (!valid() || State != BrowserState.WebDriverAttached || epoch != AttachmentEpoch) throw new StaleAttachmentException();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
        using var deadline = new Deadline(options.Timeouts.Command, linked.Token);
        await commands.WaitAsync(deadline.Token);
        Exception? failure = null;
        try
        {
            if (!valid()) throw new StaleAttachmentException();
            using var entered = admission.Enter(epoch);
            return await attachment!.InvokeAsync(action, deadline.Token);
        }
        catch (Exception e) { failure = e; }
        finally { commands.Release(); }
        var mapped = Attachment.Map(failure!);
        if (failure is OperationCanceledException || mapped.Category is ErrorCategory.AttachmentLost or ErrorCategory.OperationTimedOut)
        {
            await lifecycle.WaitAsync();
            try { if (epoch == AttachmentEpoch) await RemoveFailedAttachmentAsync(); }
            finally { lifecycle.Release(); }
        }
        if (failure is OperationCanceledException)
        {
            caller.ThrowIfCancellationRequested();
            throw new UcException(ErrorCategory.OperationTimedOut, "WebDriver command exceeded its deadline.") { Diagnostic = Health };
        }
        mapped.Diagnostic = Health; mapped.OperationId = Guid.NewGuid(); throw mapped;
    }
    private async Task RemoveFailedAttachmentAsync()
    {
        _ = admission.Close();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (attachment is not null) { await attachment.DestroyAsync(cleanup.Token); attachment = null; }
            await ProbeAsync(cleanup.Token); SetState(BrowserState.CdpOnly);
        }
        catch (Exception e) { cleanupFailures = [e.GetType().Name]; SetState(BrowserState.Faulted); }
    }
    public ValueTask<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default)
        => FindForLeaseAsync(locator, options ?? new(), AttachmentEpoch, () => true, cancellationToken);
    internal ValueTask<ElementRef> FindForLeaseAsync(Locator locator, FindOptions find, long epoch, Func<bool> valid, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var copy = find with { FramePath = find.FramePath.ToArray() };
        return CommandAsync(epoch, valid, driver =>
        {
            var target = cdp!.Resolve(copy.Target).Key;
            RestoreElementContext(driver, target, copy.FramePath);
            _ = driver.FindElement(Attachment.By(locator));
            var id = attachment!.Executor.LastFoundElementId ?? throw new UcException(ErrorCategory.ProtocolError, "W3C find-element response did not contain an element reference.");
            return new ElementRef(this, id, locator, copy, target, SessionGeneration, epoch);
        }, token);
    }
    internal void RestoreElementContext(RemoteWebDriver driver, TargetKey target, IReadOnlyList<Locator> framePath)
    {
        cdp!.Resolve(target);
        string? handle;
        lock (windows) windows.TryGetValue(target, out handle);
        if (handle is null) throw new UcException(ErrorCategory.AmbiguousTarget, "Select the target before locating elements in it.");
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
            await lifecycle.WaitAsync(cleanup.Token); held = true;
            SetState(BrowserState.Disposing);
            try { await StageAsync("cleanup", cleanup.Token); }
            catch (Exception e) { failures.Add($"cleanup observer: {e.GetType().Name}"); }
            var drained = admission.Close();
            async Task Step(string name, Func<CancellationToken, Task> action, TimeSpan? limit = null)
            {
                try { using var deadline = new Deadline(limit ?? TimeSpan.FromSeconds(10), cleanup.Token); await action(deadline.Token); }
                catch (Exception e) { failures.Add($"{name}: {e.GetType().Name}"); }
            }
            await Step("command drain", ct => drained.WaitAsync(ct), this.options.Timeouts.DisconnectDrain);
            if (attachment is not null)
            {
                await Step("driver", async ct => { await attachment.DestroyAsync(ct); attachment = null; }, this.options.Timeouts.ForceKillWait);
            }
            if (chrome is not null)
            {
                if (chrome.Alive && cdp?.Healthy == true)
                {
                    try
                    {
                        using var graceful = new Deadline(this.options.Timeouts.GracefulShutdown, cleanup.Token);
                        try { await cdp.BrowserAsync("Browser.close", null, graceful.Token); } catch (UcException) { /* Browser.close may close the socket before replying. */ }
                        await chrome.WaitAsync(graceful.Token);
                    }
                    catch (OperationCanceledException) { diagnostics.Write(1030, "Graceful Chrome shutdown timed out; terminating the owned job."); }
                }
                await Step("chrome", async ct => { chrome.Terminate(); await chrome.WaitAsync(ct); }, this.options.Timeouts.ForceKillWait);
            }
            if (cdp is not null) await Step("cdp", async ct => { await cdp.DisposeAsync().AsTask().WaitAsync(ct); cdp = null; });
            if (profile is not null) await Step("profile", async ct => { await profile.CleanupAsync(chrome?.TreeAlive != true, ct); profile = null; }, this.options.Timeouts.ProfileCleanup);
            if (chrome is not null && !chrome.TreeAlive) { chrome.Dispose(); chrome = null; }
            await Step("diagnostics", async ct => await diagnostics.DisposeAsync().AsTask().WaitAsync(ct));
        }
        catch (Exception e) { failures.Add($"cleanup deadline: {e.GetType().Name}"); }
        finally
        {
            cleanupFailures = failures.ToArray();
            state = failures.Count == 0 ? BrowserState.Stopped : BrowserState.Faulted;
            if (held) lifecycle.Release();
        }
        if (failures.Count > 0) throw new UcException(ErrorCategory.CleanupIncomplete, "Browser cleanup was incomplete.") { Diagnostic = Health, CleanupFailures = cleanupFailures };
    }
    public ValueTask DisposeAsync() => StopAsync();
}
