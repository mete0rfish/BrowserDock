using System.Diagnostics;
using BrowserDock.Cdp;

namespace BrowserDock;

public sealed partial class Browser
{
    /// <summary>Opt-in UC helpers over this browser. Access does not change startup options or transfer ownership.</summary>
    public UcBrowser Uc => new(this);
    internal ValueTask<WebDriverLease> UcReconnectAsync(TimeSpan duration, CancellationToken caller)
        => OperationAsync(async token =>
        {
            await RunUcAsync(UcOperation.Reconnect, null, new(), duration, options.Timeouts.Reconnect, token, caller).ConfigureAwait(false);
            return NewLease();
        }, options.Timeouts.Reconnect, caller);
    internal ValueTask<NavigationResult> UcOpenAsync(UcOperation operation, Uri url, UcNavigationOptions navigation, TimeSpan duration, CancellationToken caller)
    {
        ValidateUrl(url);
        return OperationAsync(async token =>
        {
            var page = (await RunUcAsync(operation, url, navigation, duration, options.Timeouts.Navigation, token, caller).ConfigureAwait(false))!;
            return new NavigationResult(page.Url, page.Redirects, page.Outcome, SessionGeneration, AttachmentEpoch);
        }, options.Timeouts.Navigation, caller);
    }
    private async Task<PageResult?> RunUcAsync(UcOperation operation, Uri? url, UcNavigationOptions navigation, TimeSpan duration,
        TimeSpan budget, CancellationToken token, CancellationToken caller)
    {
        try { return await UcWorkflow.RunAsync(new UcContext(this), operation, url, navigation, duration, budget, token, caller).ConfigureAwait(false); }
        catch
        {
            if (chrome is not null && !chrome.Alive) SetState(BrowserState.Faulted);
            throw;
        }
    }
    private sealed class UcContext(Browser browser) : IUcContext
    {
        public BrowserState State => browser.State;
        public async Task PrepareAsync(TargetKey? target, CancellationToken token)
        {
            await browser.ProbeAsync(token).ConfigureAwait(false);
            if (target is not null) await browser.cdp!.SelectAsync(target.Value, token).ConfigureAwait(false);
            if (browser.cdp!.Snapshot().Count > 1 && !browser.cdp.ExplicitSelection) throw new AmbiguousTargetException();
            browser.cdp.Resolve();
        }
        public async Task<long> DetachAsync(CancellationToken token)
        {
            long disconnected = 0;
            await browser.DisconnectCoreAsync(token, () => disconnected = Stopwatch.GetTimestamp()).ConfigureAwait(false);
            return disconnected;
        }
        public Task ReplaceAsync(CancellationToken token) => browser.cdp!.ReplaceAsync(token);
        public Task<PageResult> NavigateAsync(Uri url, NavigationWaitUntil wait, CancellationToken token)
            => browser.cdp!.NavigateAsync(url, new() { Mode = NavigationMode.CdpOnly, Target = browser.cdp.Controlled, WaitUntil = wait }, null, token);
        public async Task AttachAsync(CancellationToken token)
        {
            using var deadline = new Deadline(browser.options.Timeouts.Reconnect, token);
            await browser.AttachCoreAsync(true, deadline.Token).ConfigureAwait(false);
        }
        public TimeSpan Elapsed(long since) => TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - since) / (double)Stopwatch.Frequency);
        public Task DelayAsync(TimeSpan duration, CancellationToken token) => Task.Delay(duration, token);
        public ValueTask StageAsync(string stage, CancellationToken token) => browser.StageAsync(stage, token);
    }
}
