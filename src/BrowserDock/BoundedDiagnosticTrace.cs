using System.Diagnostics;
using System.Text.Json;

namespace BrowserDock;

// Only explicitly selected metadata belongs here, never URLs, headers or script bodies.
internal sealed class BoundedDiagnosticTrace
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<Dictionary<string, string>> entries = new();
    internal void Add(string name, params (string Key, string? Value)[] fields)
    {
        var entry = new Dictionary<string, string>
        {
            ["ms"] = clock.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["event"] = Clip(name)
        };
        foreach (var field in fields.Take(12)) if (field.Value is not null) entry[field.Key] = Clip(field.Value);
        lock (entries)
        {
            if (entries.Count == 64) entries.Dequeue();
            entries.Enqueue(entry);
        }
    }
    internal string Snapshot() { lock (entries) return JsonSerializer.Serialize(entries.ToArray()); }
    private static string Clip(string value) => value.Length <= 128 ? value : value.Substring(0, 128);
    internal static string? Field(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) &&
            field.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? field.ToString() : null;
    internal void Cdp(string method, string? session, JsonElement parameters)
    {
        var frame = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("frame", out var nested) ? nested : parameters;
        Add(method, ("session", session), ("affectedSession", Field(parameters, "sessionId")),
            ("target", Field(parameters, "targetId")), ("frame", Field(parameters, "frameId") ?? Field(frame, "id")),
            ("loader", Field(frame, "loaderId")), ("request", Field(parameters, "requestId")),
            ("type", Field(parameters, "type")), ("name", Field(parameters, "name")),
            ("error", Field(parameters, "errorText")), ("canceled", Field(parameters, "canceled")),
            ("reason", Field(parameters, "reason")), ("blockedReason", Field(parameters, "blockedReason")));
    }
}
