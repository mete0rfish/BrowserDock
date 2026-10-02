using System.Collections;

namespace BrowserDock;

internal static class NavigationDiagnostics
{
    internal const string Prefix = "Navigation.";
    internal static void Record(Exception error, string stage, NavigationOptions options)
    {
        // Preserve the more specific CDP stage when the Browser layer adds context.
        if (!error.Data.Contains(Prefix + "Stage")) error.Data[Prefix + "Stage"] = stage;
        error.Data[Prefix + "Mode"] = options.Mode.ToString();
        error.Data[Prefix + "WaitUntil"] = options.WaitUntil.ToString();
    }
    internal static void Copy(Exception source, Exception destination)
    {
        if (ReferenceEquals(source, destination)) return;
        foreach (DictionaryEntry entry in source.Data)
            if (entry.Key is string key && key.StartsWith(Prefix, StringComparison.Ordinal) && entry.Value is string value)
                destination.Data[key] = value;
    }
}
