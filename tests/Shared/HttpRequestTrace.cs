using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BrowserDock.TestFixtures;

// Local fixture routes only. Never capture query strings, headers or bodies.
internal sealed class HttpRequestTrace(Action<string>? output = null)
{
    internal const string Prefix = "BROWSERDOCK_HTTP_TRACE=";
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<string> entries = new();
    private long sequence;
    internal string[] Snapshot() { lock (entries) return entries.ToArray(); }
    internal async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var path = context.Request.Path.Value ?? "";
        if (!(path.StartsWith("/reference/", StringComparison.Ordinal) || path is "/page" or "/frame" or "/redirect" or "/download" or "/slow"))
        { await next(); return; }
        var id = Interlocked.Increment(ref sequence);
        void Record(string phase, string? exception = null)
        {
            var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow.ToString("O"), ms = clock.ElapsedMilliseconds, id, phase, exception,
                method = context.Request.Method, path = path.Length <= 128 ? path : path.Substring(0, 128),
                status = context.Response.StatusCode, started = context.Response.HasStarted, canceled = context.RequestAborted.IsCancellationRequested });
            lock (entries)
            {
                if (entries.Count == 64) entries.Dequeue();
                entries.Enqueue(line);
                output?.Invoke(Prefix + line);
            }
        }
        Record("start");
        string? failure = null;
        try { await next(); }
        catch (Exception error) { failure = error.GetType().Name; throw; }
        finally { Record("end", failure); }
    }
}
