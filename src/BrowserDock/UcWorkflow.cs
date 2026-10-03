using BrowserDock.Cdp;
using BrowserDock.WebDriver;

namespace BrowserDock;

internal enum UcOperation { Reconnect, Open, OpenWithTab, OpenWithReconnect, OpenWithDisconnect }

// The workflow owns ordering; the Browser adapter owns actual resources. A
// deterministic clock/transport fixture exercises the same workflow on net481.
internal interface IUcContext
{
    BrowserState State { get; }
    Task PrepareAsync(TargetKey? target, CancellationToken token);
    Task<long> DetachAsync(CancellationToken token);
    Task ReplaceAsync(CancellationToken token);
    Task<PageResult> NavigateAsync(Uri url, NavigationWaitUntil wait, CancellationToken token);
    Task AttachAsync(CancellationToken token);
    TimeSpan Elapsed(long since);
    Task DelayAsync(TimeSpan duration, CancellationToken token);
    ValueTask StageAsync(string stage, CancellationToken token);
}

internal static class UcWorkflow
{
    internal static void Validate(BrowserState state, UcNavigationOptions options, TimeSpan duration, TimeSpan budget)
    {
        if (state is not (BrowserState.WebDriverAttached or BrowserState.CdpOnly))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "UC operations require WebDriverAttached or CdpOnly state.");
        if (!Enum.IsDefined(typeof(NavigationWaitUntil), options.WaitUntil))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "Invalid UC navigation completion condition.");
        if (duration < TimeSpan.Zero || duration >= budget)
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "Minimum disconnected duration must be nonnegative and less than the operation timeout.");
    }
    internal static async Task<PageResult?> RunAsync(IUcContext context, UcOperation operation, Uri? url, UcNavigationOptions options,
        TimeSpan duration, TimeSpan budget, CancellationToken token, CancellationToken caller)
    {
        Validate(context.State, options, duration, budget);
        var initiallyAttached = context.State == BrowserState.WebDriverAttached;
        var attach = operation is UcOperation.Reconnect or UcOperation.OpenWithReconnect ||
            (initiallyAttached && operation is UcOperation.Open or UcOperation.OpenWithTab);
        var replace = operation is UcOperation.OpenWithTab or UcOperation.OpenWithReconnect or UcOperation.OpenWithDisconnect;
        var advanced = false;
        var stage = "uc-prepare";
        async Task Stage(string value)
        {
            stage = value;
            token.ThrowIfCancellationRequested();
            await context.StageAsync(value, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        try
        {
            await Stage("uc-prepare").ConfigureAwait(false);
            await context.PrepareAsync(options.Target, token).ConfigureAwait(false);
            await Stage("uc-disconnect").ConfigureAwait(false);
            advanced = initiallyAttached;
            var disconnected = await context.DetachAsync(token).ConfigureAwait(false);
            await Stage("uc-detached").ConfigureAwait(false);
            if (replace)
            {
                await Stage("uc-replace").ConfigureAwait(false);
                advanced = true;
                await context.ReplaceAsync(token).ConfigureAwait(false);
            }
            PageResult? result = null;
            if (url is not null)
            {
                await Stage("uc-navigation").ConfigureAwait(false);
                advanced = true;
                result = await context.NavigateAsync(url, options.WaitUntil, token).ConfigureAwait(false);
                await Stage("uc-navigation-completed").ConfigureAwait(false);
            }
            await Stage("uc-hold").ConfigureAwait(false);
            // Navigation, replacement and post-exit probes already count toward
            // the minimum. Never add a second full duration after page load.
            TimeSpan remaining;
            while ((remaining = duration - context.Elapsed(disconnected)) > TimeSpan.Zero)
                await context.DelayAsync(remaining, token).ConfigureAwait(false);
            if (attach)
            {
                await Stage("uc-reconnect").ConfigureAwait(false);
                advanced = true;
                await context.AttachAsync(token).ConfigureAwait(false);
            }
            return result;
        }
        catch (Exception original)
        {
            NavigationDiagnostics.Record(original, stage, new() { Mode = NavigationMode.Detached, WaitUntil = options.WaitUntil });
            original.Data[NavigationDiagnostics.Prefix + "UcOperation"] = operation.ToString();
            if (original is OperationCanceledException) throw new NavigationCanceledException(caller, advanced, original);
            var error = Attachment.Map(original);
            NavigationDiagnostics.Copy(original, error);
            error.BrowserMayHaveAdvanced |= advanced;
            throw error;
        }
    }
}
