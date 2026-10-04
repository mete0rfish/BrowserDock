using System.Text.Json;

namespace BrowserDock.Cdp;

// One ordered set per live CDP session. Caller scripts are sent verbatim and separately.
internal sealed class UcScriptRegistration
{
    private string[] sources = [];
    private readonly List<string> identifiers = new();
    private int detached;
    internal void Detach() => Interlocked.Exchange(ref detached, 1);
    internal void EnsureAttached()
    {
        if (Volatile.Read(ref detached) != 0)
            throw new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "The document-script session was detached.");
    }
    internal async Task ReconcileAsync(IReadOnlyList<string> desired,
        Func<string, object, CancellationToken, Task<JsonElement>> send, Action invalidate, CancellationToken token, bool forceOrder = false)
    {
        EnsureAttached();
        if (!forceOrder && sources.SequenceEqual(desired, StringComparer.Ordinal)) return;
        var mutated = false;
        try
        {
            foreach (var identifier in identifiers)
            {
                token.ThrowIfCancellationRequested();
                EnsureAttached(); mutated = true;
                await send("Page.removeScriptToEvaluateOnNewDocument", new { identifier }, token).ConfigureAwait(false);
            }
            identifiers.Clear();
            foreach (var source in desired)
            {
                token.ThrowIfCancellationRequested();
                EnsureAttached(); mutated = true;
                var reply = await send("Page.addScriptToEvaluateOnNewDocument", new { source }, token).ConfigureAwait(false);
                EnsureAttached();
                if (!reply.TryGetProperty("identifier", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(id.GetString()))
                    throw new BrowserDockException(ErrorCategory.ProtocolError, "Chrome did not identify the registered document script.");
                identifiers.Add(id.GetString()!);
            }
            EnsureAttached();
            sources = desired.ToArray();
        }
        catch
        {
            // A canceled/failed command might already have registered a script. Never retry it
            // against this session; transport recovery creates fresh session-owned registrations.
            if (mutated) { Detach(); invalidate(); }
            throw;
        }
    }
}
