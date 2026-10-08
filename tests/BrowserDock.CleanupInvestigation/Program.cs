using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

// Windows Framework investigation only. Emit method signatures, never memory,
// addresses, locals, arguments, environment variables or temporary dump files.
if (!OperatingSystem.IsWindows() || args.Length != 1 || !int.TryParse(args[0], out var pid) || pid <= 0)
{
    Console.Error.WriteLine("Usage on Windows: BrowserDock.CleanupInvestigation <owned-testhost-pid>");
    return 2;
}
try
{
    using var target = DataTarget.CreateSnapshotAndAttach(pid);
    var evidence = new List<object>();
    foreach (var version in target.ClrVersions)
    {
        using var runtime = version.CreateRuntime();
        var threads = runtime.Threads.Where(thread => thread.IsAlive).Take(128).Select(thread => new
        {
            managedThreadId = thread.ManagedThreadId,
            frames = thread.EnumerateStackTrace().Take(64).Select(frame =>
                frame.Method?.Signature ?? "[" + frame.Kind + "]").ToArray()
        }).ToArray();
        evidence.Add(new { runtime = version.Version.ToString(), threads });
    }
    if (evidence.Count == 0) throw new InvalidOperationException("No CLR found in the owned test host.");
    Console.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name);
    return 1;
}
