using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace BrowserDock.TestFixtures;

internal static class CleanupThreadEvidence
{
    internal static string Collect()
    {
        if (!Platform.IsWindows) throw new PlatformNotSupportedException();
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BrowserDock.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository cleanup collector not found.");
        var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        var tool = Path.Combine(root.FullName, "tests", "BrowserDock.CleanupInvestigation", "bin", configuration,
            "net10.0", "BrowserDock.CleanupInvestigation.dll");
        if (!File.Exists(tool)) throw new FileNotFoundException("Build the cleanup investigation tool first.");
        using var parent = Process.GetCurrentProcess();
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        RuntimeCompatibility.SetArguments(info, new[] { tool, parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var collector = Process.Start(info) ?? throw new InvalidOperationException("Cleanup collector did not start.");
        var stdout = collector.StandardOutput.ReadToEndAsync(); var stderr = collector.StandardError.ReadToEndAsync();
        if (!collector.WaitForExit(15000)) { collector.Kill(); collector.WaitForExit(5000); throw new TimeoutException("Cleanup stack collection timed out."); }
        if (!Task.WaitAll(new Task[] { stdout, stderr }, 5000)) throw new TimeoutException("Cleanup collector output did not finish.");
        if (collector.ExitCode != 0) throw new InvalidOperationException("Cleanup collector failed: " + stderr.Result.Trim());
        using var json = JsonDocument.Parse(stdout.Result);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Cleanup collector returned no runtime evidence.");
        return stdout.Result;
    }

    internal static void Capture(Exception error)
    {
        var directory = Environment.GetEnvironmentVariable("BROWSERDOCK_CLEANUP_EVIDENCE");
        if (!Platform.IsWindows || string.IsNullOrEmpty(directory) || error is not TimeoutException) return;
        try
        {
            Directory.CreateDirectory(directory);
            var name = "cleanup-stacks-" + Guid.NewGuid().ToString("N") + ".json";
            File.WriteAllText(Path.Combine(directory, name), Collect());
            error.Data["Cleanup.ThreadEvidence"] = name;
            TestContext.Out.WriteLine("Cleanup.ThreadEvidence: " + name);
        }
        catch (Exception collectionError)
        {
            error.Data["Cleanup.ThreadEvidenceError"] = collectionError.GetType().Name;
            TestContext.Out.WriteLine("Cleanup.ThreadEvidenceError: " + collectionError.GetType().Name);
        }
    }
}
