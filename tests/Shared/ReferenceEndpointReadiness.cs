using System.Globalization;

namespace BrowserDock.TestFixtures;

// Chromium creates DevToolsActivePort before writing its contents. File
// existence alone is not readiness; keep using the caller's startup deadline.
internal static class ReferenceEndpointReadiness
{
    internal static async Task<Uri> WaitAsync(string path, Func<bool> alive,
        CancellationToken token, Func<string, string>? read = null)
    {
        read ??= File.ReadAllText;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!alive()) throw new InvalidOperationException("Owned Chrome exited before endpoint readiness.");
            string? contents = null;
            try { contents = read(path); }
            catch (FileNotFoundException) { } // Not published yet, or removed while reading.
            token.ThrowIfCancellationRequested();
            if (!alive()) throw new InvalidOperationException("Owned Chrome exited before endpoint readiness.");
            if (contents is not null && TryParse(contents, out var endpoint)) return endpoint!;
            await Task.Delay(20, token);
        }
    }

    private static bool TryParse(string contents, out Uri? endpoint)
    {
        endpoint = null;
        var lines = contents.Split('\n');
        if (lines.Length < 2 || !int.TryParse(lines[0].TrimEnd('\r'), NumberStyles.None,
                CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535) return false;
        var path = lines[1].TrimEnd('\r');
        const string prefix = "/devtools/browser/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(path.Substring(prefix.Length), "D", out _)) return false;
        return Uri.TryCreate("ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + path,
            UriKind.Absolute, out endpoint);
    }
}
