using System.Collections;
using System.Text.Json;
using NUnit.Framework;

namespace BrowserDock.TestFixtures;

internal static class FailureEvidence
{
    internal static async Task<T> CaptureAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception error) { Write(error); throw; }
    }
    internal static void Write(Exception error)
    {
        var pending = new Queue<Exception>(); pending.Enqueue(error);
        for (var count = 0; count < 16 && pending.Count > 0; count++)
        {
            var item = pending.Dequeue();
            var context = item.Data.Cast<DictionaryEntry>()
                .Where(entry => entry.Key is string key && (key.StartsWith("Navigation.", StringComparison.Ordinal) || key.StartsWith("Cleanup.", StringComparison.Ordinal)) && entry.Value is string)
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new { exception = item.GetType().Name, item.HResult, context }));
            if (item is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) pending.Enqueue(inner);
            else if (item.InnerException is not null) pending.Enqueue(item.InnerException);
            if (item.Data["CleanupFailure"] is Exception cleanup) pending.Enqueue(cleanup);
        }
    }
    internal static void DeleteDirectory(string path, Exception? primary)
    {
        try { Directory.Delete(path, true); }
        catch (Exception cleanup) when (primary is not null)
        {
            primary.Data["TestCleanupFailure"] = cleanup;
            Write(cleanup);
            TestContext.Out.WriteLine("Test directory cleanup also failed: " + cleanup);
        }
    }
}
