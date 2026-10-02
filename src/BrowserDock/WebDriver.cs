using System.Net;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using BrowserDock.Hosting;

namespace BrowserDock.WebDriver;

internal sealed class LoopbackExecutor(Uri address, TimeSpan timeout) : HttpCommandExecutor(address, timeout)
{
    protected override HttpClientHandler CreateHttpClientHandler() => new() { UseProxy = false, AllowAutoRedirect = false };
}
internal sealed class DetachAwareCommandExecutor(ICommandExecutor inner) : ICommandExecutor
{
    private int detaching;
    private readonly object disposalSync = new();
    private bool disposed;
    private long sent;
    internal long Sent => Interlocked.Read(ref sent);
    public string? LastFoundElementId { get; private set; }
    public Func<bool>? IsCurrent { get; set; }
    public void Detach() => Interlocked.Exchange(ref detaching, 1);
    public bool TryAddCommand(string commandName, CommandInfo? info) => inner.TryAddCommand(commandName, info);
    public Response Execute(Command commandToExecute)
    {
        if (commandToExecute.Name == DriverCommand.Quit) return new Response(commandToExecute.SessionId?.ToString(), null, WebDriverResult.Success);
        Check(); Interlocked.Increment(ref sent); return Capture(commandToExecute, inner.Execute(commandToExecute));
    }
    public async Task<Response> ExecuteAsync(Command commandToExecute)
    {
        if (commandToExecute.Name == DriverCommand.Quit) return new Response(commandToExecute.SessionId?.ToString(), null, WebDriverResult.Success);
        Check(); Interlocked.Increment(ref sent); return Capture(commandToExecute, await inner.ExecuteAsync(commandToExecute).ConfigureAwait(false));
    }
    private Response Capture(Command command, Response response)
    {
        if (command.Name == DriverCommand.FindElement)
        {
            var value = JsonSerializer.SerializeToElement(response.Value);
            LastFoundElementId = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("element-6066-11e4-a52e-4f735466cecf", out var id) ? id.GetString() : null;
        }
        return response;
    }
    private void Check() { if (Volatile.Read(ref detaching) != 0 || IsCurrent?.Invoke() == false) throw new StaleAttachmentException(); }
    public void Dispose()
    {
        Detach();
        lock (disposalSync)
        {
            if (disposed) return;
            inner.Dispose();
            disposed = true;
        }
    }
}
internal sealed class Attachment(OwnedProcess process, int port, DetachAwareCommandExecutor executor, RemoteWebDriver driver)
{
    private readonly object cleanupSync = new();
    private readonly object ioSync = new();
    private bool processTerminated, executorDisposed, processExited, driverDisposed, processDisposed;
    private Task? destruction;
    internal Func<Task>? BeforeDispatchForTest { get; set; }
    internal Task? InvocationForTest { get; private set; }
    internal Action? TerminateForTest { get; set; }
    internal sealed class CommandExecution
    {
        // 0: queued, 1: started, 2: canceled before start. Once an await ends,
        // a queued worker must never dispatch later against a retained attachment.
        private int state;
        public bool Started => Volatile.Read(ref state) == 1;
        public void Start(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) throw new OperationCanceledException(token);
        }
        public bool CancelBeforeStart() => Interlocked.CompareExchange(ref state, 2, 0) != 1;
    }
    // Cancellation and final disposal share one owner for the executor/process.
    private void StopIo()
    {
        lock (ioSync)
        {
            Executor.Detach();
            var failures = new List<Exception>();
            if (!processTerminated)
                try { if (TerminateForTest is { } terminate) terminate(); else Process.Terminate(); processTerminated = true; }
                catch (Exception e) { failures.Add(e); }
            if (!executorDisposed)
                try { Executor.Dispose(); executorDisposed = true; } catch (Exception e) { failures.Add(e); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
    internal static ChromeOptions CreateOptions(Uri cdp) => new()
    {
        DebuggerAddress = $"127.0.0.1:{cdp.Port}",
        // This browser is externally owned. The launch-only detach capability
        // is rejected by ChromeDriver in debuggerAddress mode.
        PageLoadStrategy = PageLoadStrategy.None
    };
    public OwnedProcess Process { get; } = process;
    public int Port { get; } = port;
    public DetachAwareCommandExecutor Executor { get; } = executor;
    public RemoteWebDriver Driver { get; } = driver;
    public string SessionId => Driver.SessionId.ToString();
    public async Task<T> InvokeAsync<T>(Func<RemoteWebDriver, T> action, CancellationToken token, CommandExecution? execution = null)
    {
        token.ThrowIfCancellationRequested();
        execution ??= new();
        using var cancellation = token.Register(() => execution.CancelBeforeStart());
        var beforeDispatch = BeforeDispatchForTest;
        var work = Task.Run(async () =>
        {
            if (beforeDispatch is not null) await beforeDispatch().ConfigureAwait(false);
            execution.Start(token);
            return action(Driver);
        });
        if (beforeDispatch is not null) InvocationForTest = work;
        try { return await work.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException original)
        {
            if (!execution.CancelBeforeStart())
                try { StopIo(); } catch (Exception cleanup) { original.Data["DriverCleanupFailure"] = cleanup; }
            _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            throw;
        }
    }
    public Task DestroyAsync(CancellationToken token)
    {
        Task owned;
        lock (cleanupSync)
        {
            if (destruction is null || destruction.IsFaulted || destruction.IsCanceled)
            {
                destruction = Task.Run(DestroyCoreAsync);
                _ = destruction.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            owned = destruction;
        }
        // A caller's budget bounds its wait. The shared owner finishes disposal;
        // Stop/Dispose can join that same work with their own remaining budget.
        return owned.WaitAsync(token);
    }
    private async Task DestroyCoreAsync()
    {
        var failures = new List<Exception>();
        try { StopIo(); } catch (Exception e) { failures.Add(e); }
        bool canWait;
        lock (ioSync) canWait = processTerminated || !Process.Alive;
        if (!processExited && canWait)
            try { await Process.WaitAsync(CancellationToken.None).ConfigureAwait(false); processExited = true; }
            catch (Exception e) { failures.Add(e); }
        if (!driverDisposed)
            try { await Task.Run(Driver.Dispose).ConfigureAwait(false); driverDisposed = true; }
            catch (Exception e) { failures.Add(e); }
        // Keep the process handle if termination failed while it is still alive,
        // so a later cleanup owner can retry the remaining work.
        if (processExited && !processDisposed)
            try { Process.Dispose(); processDisposed = true; } catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0) throw new AggregateException(failures);
    }
    public static async Task<Attachment> CreateAsync(string path, Uri cdp, BrowserTimeouts timeouts, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var port = BrowserHosting.CandidatePort();
            OwnedProcess? process = null;
            DetachAwareCommandExecutor? executor = null;
            Task<RemoteWebDriver>? create = null;
            var creatingSession = false;
            try
            {
                process = new OwnedProcess(path, [$"--port={port}", "--allowed-ips=127.0.0.1", "--allowed-origins=http://127.0.0.1"]);
                using (var readiness = new Deadline(timeouts.DriverProcessStart, token))
                using (var http = BrowserHosting.Http())
                {
                    while (true)
                    {
                        readiness.Token.ThrowIfCancellationRequested();
                        if (!process.Alive) throw new BrowserDockException(ErrorCategory.DriverProcessFailure, "ChromeDriver exited before readiness.");
                        try
                        {
                            using var result = JsonDocument.Parse(await RuntimeCompatibility.HttpStringAsync(http, $"http://127.0.0.1:{port}/status", readiness.Token).ConfigureAwait(false));
                            if (result.RootElement.GetProperty("value").GetProperty("ready").GetBoolean() && process.Alive && BrowserHosting.OwnsDriverListener(process.Id, port)) break;
                        }
                        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException) { }
                        await Task.Delay(50, readiness.Token).ConfigureAwait(false);
                    }
                }
                creatingSession = true;
                executor = new DetachAwareCommandExecutor(new LoopbackExecutor(new Uri($"http://127.0.0.1:{port}"), timeouts.Command > timeouts.WebDriverSessionCreate ? timeouts.Command : timeouts.WebDriverSessionCreate));
                var options = CreateOptions(cdp);
                var localExecutor = executor;
                create = Task.Run(() => new RemoteWebDriver(localExecutor, options.ToCapabilities()));
                using var deadline = new Deadline(timeouts.WebDriverSessionCreate, token);
                var driver = await create.WaitAsync(deadline.Token).ConfigureAwait(false);
                return new(process, port, executor, driver);
            }
            catch (Exception e)
            {
                last = e;
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    executor?.Detach();
                    process?.Terminate(); executor?.Dispose();
                    if (process is not null) { await process.WaitAsync(cleanup.Token).ConfigureAwait(false); process.Dispose(); }
                    if (create is not null)
                    {
                        try { var late = await create.WaitAsync(cleanup.Token).ConfigureAwait(false); late.Dispose(); }
                        catch (Exception createError) when (createError is not OperationCanceledException || !cleanup.IsCancellationRequested) { }
                    }
                }
                catch (Exception cleanupError) { throw new BrowserDockException(ErrorCategory.CleanupIncomplete, "Attachment creation cleanup did not finish.", e) { CleanupFailures = [cleanupError.GetType().Name] }; }
                token.ThrowIfCancellationRequested();
                if (creatingSession) throw new BrowserDockException(ErrorCategory.WebDriverSessionFailure, "Could not create a WebDriver session.", e) { Retryable = true };
            }
        }
        throw new BrowserDockException(ErrorCategory.DriverProcessFailure, "ChromeDriver failed readiness after three port attempts.", last);
    }
    internal static By By(Locator locator) => locator.Kind switch
    {
        LocatorKind.Css => OpenQA.Selenium.By.CssSelector(locator.Value), LocatorKind.XPath => OpenQA.Selenium.By.XPath(locator.Value), LocatorKind.Id => OpenQA.Selenium.By.Id(locator.Value),
        LocatorKind.Name => OpenQA.Selenium.By.Name(locator.Value), LocatorKind.TagName => OpenQA.Selenium.By.TagName(locator.Value), LocatorKind.LinkText => OpenQA.Selenium.By.LinkText(locator.Value),
        _ => throw new BrowserDockException(ErrorCategory.ConfigurationError, "Unknown locator kind.")
    };
    internal static BrowserDockException Map(Exception e) => e switch
    {
        BrowserDockException error => error,
        StaleElementReferenceException => new(ErrorCategory.StaleDomElement, "The DOM node is stale. Explicitly reacquire it.", e),
        WebDriverTimeoutException => new(ErrorCategory.OperationTimedOut, "WebDriver command timed out.", e),
        WebDriverException when e.Message.Contains("operation not supported", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("unsupported operation", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("operation is unsupported", StringComparison.OrdinalIgnoreCase) => new(ErrorCategory.UnsupportedAttachedCommand, "This command is unsupported by the attached browser.", e),
        NoSuchWindowException => new(ErrorCategory.TargetClosed, "WebDriver window was closed.", e),
        NoSuchElementException => new(ErrorCategory.ElementNotFound, "The locator did not match an element.", e),
        InvalidSelectorException or ArgumentException => new(ErrorCategory.ConfigurationError, "WebDriver rejected a command argument.", e),
        InvalidElementStateException => new(ErrorCategory.ElementInteractionFailure, "The element cannot perform this interaction in its current state.", e),
        WebDriverException => new(ErrorCategory.AttachmentLost, "WebDriver command failed.", e),
        _ => new(ErrorCategory.ProtocolError, "Browser operation failed.", e)
    };
}
