using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UcDotNet.Hosting;

namespace UcDotNet.Patching;

public sealed record PatchPattern(string Id, byte[] Search, byte[] Replacement, int ExpectedCount);
public interface IDriverPatchStrategy
{
    string RecipeId { get; }
    bool Supports(Version driverVersion);
    IReadOnlyList<PatchPattern> Patterns { get; }
}
internal sealed record PatchManifest(int Schema, string SourceHash, string ResultHash, string RecipeId, string Version, Dictionary<string, int> Counts);
internal static class DriverPatchCache
{
    public static Task<string> PrepareAsync(DriverArtifactOptions options, Version version, DriverPatchMode mode, CancellationToken token)
        => Task.Run(() => Prepare(options, version, mode, token), token);
    private static string Prepare(DriverArtifactOptions options, Version version, DriverPatchMode mode, CancellationToken token)
    {
        var source = Paths.Canonical(options.ExecutablePath);
        if (mode == DriverPatchMode.Disabled) return source;
        var matches = options.PatchStrategies.Where(x => x.Supports(version)).ToArray();
        if (matches.Length != 1) throw Mismatch("Exactly one verified recipe must support this driver version.");
        var strategy = matches[0];
        var original = File.ReadAllBytes(source);
        var sourceHash = Hash(original);
        var result = Apply(original, strategy, out var counts);
        if (mode == DriverPatchMode.ValidateOnly) return source;
        var resultHash = Hash(result);
        var root = Paths.Canonical(options.CacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UcDotNet", "drivers"));
        var key = Hash(Encoding.UTF8.GetBytes($"{sourceHash}:{version}:{strategy.RecipeId}"));
        Directory.CreateDirectory(root);
        using var mutex = new Mutex(false, "UcDotNet.Patch." + key);
        var held = false;
        string? staging = null;
        try
        {
            try { while (!(held = mutex.WaitOne(100))) token.ThrowIfCancellationRequested(); }
            catch (AbandonedMutexException) { held = true; }
            token.ThrowIfCancellationRequested();
            var directory = Paths.Canonical(Path.Combine(root, key));
            var executable = Path.Combine(directory, Path.GetFileName(source));
            if (Directory.Exists(directory))
            {
                PatchManifest manifest;
                try { manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")))!; }
                catch (Exception e) when (e is IOException or JsonException) { throw Mismatch("Patch cache manifest is unreadable."); }
                if (manifest is null || manifest.Schema != 1 || manifest.SourceHash != sourceHash || manifest.ResultHash != resultHash || manifest.RecipeId != strategy.RecipeId || manifest.Version != version.ToString() || !File.Exists(executable) || Hash(File.ReadAllBytes(Paths.Canonical(executable))) != resultHash || manifest.Counts.Count != counts.Count || counts.Any(x => !manifest.Counts.TryGetValue(x.Key, out var n) || n != x.Value))
                    throw Mismatch("Patch cache integrity validation failed.");
                return executable;
            }
            staging = Path.Combine(root, key + ".partial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var resultPath = Path.Combine(staging, Path.GetFileName(source));
            File.WriteAllBytes(resultPath, result);
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(new PatchManifest(1, sourceHash, resultHash, strategy.RecipeId, version.ToString(), counts)));
            if (Hash(File.ReadAllBytes(source)) != sourceHash) throw Mismatch("Source driver changed during patching.");
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, directory);
            staging = null;
            return executable;
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, true);
            if (held) mutex.ReleaseMutex();
        }
    }
    internal static byte[] Apply(byte[] original, IDriverPatchStrategy strategy, out Dictionary<string, int> counts)
    {
        if (string.IsNullOrWhiteSpace(strategy.RecipeId) || strategy.Patterns.Count == 0) throw Mismatch("Recipe metadata is missing.");
        counts = new(StringComparer.Ordinal);
        var edits = new List<(int Offset, byte[] Bytes)>();
        foreach (var pattern in strategy.Patterns)
        {
            if (pattern.Search.Length == 0 || pattern.Search.Length != pattern.Replacement.Length || pattern.ExpectedCount <= 0 || counts.ContainsKey(pattern.Id)) throw Mismatch("Recipe requires unique patterns and length-preserving replacements.");
            var count = 0;
            for (var offset = 0; offset <= original.Length - pattern.Search.Length; offset++)
            {
                if (!original.AsSpan(offset, pattern.Search.Length).SequenceEqual(pattern.Search)) continue;
                if (edits.Any(x => offset < x.Offset + x.Bytes.Length && x.Offset < offset + pattern.Search.Length)) throw Mismatch("Recipe patterns overlap.");
                edits.Add((offset, pattern.Replacement.ToArray())); count++;
            }
            counts.Add(pattern.Id, count);
            if (count != pattern.ExpectedCount) throw Mismatch($"Recipe pattern {pattern.Id} expected {pattern.ExpectedCount} matches; observed {count}.");
        }
        var copy = original.ToArray();
        foreach (var edit in edits) edit.Bytes.CopyTo(copy, edit.Offset);
        return copy;
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static UcException Mismatch(string message) => new(ErrorCategory.PatchMismatch, message);
}
