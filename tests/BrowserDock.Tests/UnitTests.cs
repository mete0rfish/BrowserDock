using System.Text;
using NUnit.Framework;
using BrowserDock.Hosting;
using BrowserDock.Patching;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class UnitTests
{
    [Test]
    public async Task ClosingAdmissionInvalidatesOldReferencesBeforeDrainCompletes()
    {
        var guard = new Admission();
        var epoch = guard.Open();
        var running = guard.Enter(epoch);
        var drained = guard.Close();
        Assert.That(drained.IsCompleted, Is.False);
        Assert.Throws<StaleAttachmentException>(() => guard.Enter(epoch));
        running.Dispose();
        await drained.WaitAsync(TimeSpan.FromSeconds(1));
        var next = guard.Open();
        Assert.That(next, Is.GreaterThan(epoch));
        Assert.Throws<StaleAttachmentException>(() => guard.Enter(epoch));
        using var fresh = guard.Enter(next);
    }
    [Test]
    public async Task AdmissionRacesNeverPermitCallsAfterClosure()
    {
        for (var i = 0; i < 100; i++)
        {
            var guard = new Admission(); var epoch = guard.Open();
            var calls = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            {
                try { using var admitted = guard.Enter(epoch); }
                catch (StaleAttachmentException) { }
            })).ToArray();
            await guard.Close(); await Task.WhenAll(calls);
            Assert.Throws<StaleAttachmentException>(() => guard.Enter(epoch));
        }
    }
    [TestCase("150.0.1234.1", "150.0.1234.2", true)]
    [TestCase("150.0.1234.1", "150.0.1235.1", false)]
    [TestCase("114.0.1234.1", "114.0.1234.1", false)]
    public void VersionsRequireExactBuild(string chrome, string driver, bool accepted)
    {
        if (accepted) Assert.DoesNotThrow(() => BrowserHosting.RequireMatchingVersions(Version.Parse(chrome), Version.Parse(driver)));
        else Assert.That(Assert.Throws<BrowserDockException>(() => BrowserHosting.RequireMatchingVersions(Version.Parse(chrome), Version.Parse(driver)))!.Category, Is.EqualTo(ErrorCategory.VersionMismatch));
    }
    [TestCase(BrowserState.WebDriverAttached, NavigationMode.Standard, false, true)]
    [TestCase(BrowserState.CdpOnly, NavigationMode.Standard, false, false)]
    [TestCase(BrowserState.WebDriverAttached, NavigationMode.CdpOnly, false, false)]
    [TestCase(BrowserState.CdpOnly, NavigationMode.CdpOnly, true, false)]
    [TestCase(BrowserState.CdpOnly, NavigationMode.CdpOnly, false, true)]
    [TestCase(BrowserState.CdpOnly, NavigationMode.Detached, true, true)]
    public void NavigationMatrixRejectsContradictions(BrowserState state, NavigationMode mode, bool reconnect, bool accepted)
    {
        var options = new NavigationOptions { Mode = mode, ReconnectAfterNavigation = reconnect };
        if (accepted) Assert.DoesNotThrow(() => Browser.ValidateNavigation(state, options));
        else Assert.Throws<BrowserDockException>(() => Browser.ValidateNavigation(state, options));
    }
    internal sealed class Recipe(int count = 1) : IDriverPatchStrategy
    {
        public string RecipeId => "synthetic-test-v1";
        public bool Supports(Version version) => version.Major == 150;
        public IReadOnlyList<PatchPattern> Patterns => [new("literal", Encoding.ASCII.GetBytes("ORIGINAL"), Encoding.ASCII.GetBytes("REPLACED"), count)];
    }
    [Test]
    public void PatchPreservesSourceAndFailsOnUnexpectedCounts()
    {
        var bytes = Encoding.ASCII.GetBytes("prefix ORIGINAL suffix");
        var patched = DriverPatchCache.Apply(bytes, new Recipe(), out var counts);
        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(bytes), Is.EqualTo("prefix ORIGINAL suffix"));
            Assert.That(Encoding.ASCII.GetString(patched), Is.EqualTo("prefix REPLACED suffix"));
            Assert.That(counts["literal"], Is.EqualTo(1));
        });
        Assert.Throws<BrowserDockException>(() => DriverPatchCache.Apply(bytes, new Recipe(2), out _));
        Assert.Throws<BrowserDockException>(() => DriverPatchCache.Apply(Encoding.ASCII.GetBytes("unknown"), new Recipe(), out _));
    }
    [Test]
    public async Task PatchCacheIsAtomicIdempotentAndDetectsTampering()
    {
        var root = TestPaths.NewDirectory();
        try
        {
            var original = Path.Combine(root, "driver.exe");
            await File.WriteAllTextAsync(original, "prefix ORIGINAL suffix");
            var options = new DriverArtifactOptions { ExecutablePath = original, CacheDirectory = Path.Combine(root, "cache"), PatchStrategies = [new Recipe()] };
            var outputs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DriverPatchCache.PrepareAsync(options, new Version(150, 0, 1, 0), DriverPatchMode.BinaryCompatibility, default)));
            Assert.That(outputs.Distinct().Count(), Is.EqualTo(1));
            Assert.That(await File.ReadAllTextAsync(original), Is.EqualTo("prefix ORIGINAL suffix"));
            Assert.That(Directory.GetDirectories(options.CacheDirectory, "*.partial-*"), Is.Empty);
            await File.WriteAllTextAsync(outputs[0], "tampered");
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await DriverPatchCache.PrepareAsync(options, new Version(150, 0, 1, 0), DriverPatchMode.BinaryCompatibility, default))!.Category, Is.EqualTo(ErrorCategory.PatchMismatch));
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public async Task UnknownRecipeFailsClosedWhileDisabledNeedsNoRecipe()
    {
        var options = new DriverArtifactOptions { ExecutablePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "driver.exe") };
        Assert.That(await DriverPatchCache.PrepareAsync(options, new Version(150, 0), DriverPatchMode.Disabled, default), Is.EqualTo(options.ExecutablePath));
        Assert.ThrowsAsync<BrowserDockException>(async () => await DriverPatchCache.PrepareAsync(options, new Version(150, 0), DriverPatchMode.BinaryCompatibility, default));
    }
    [Test]
    public async Task CallerProfileIsExclusivelyLockedAndPreservesSentinel()
    {
        var root = TestPaths.NewDirectory();
        try
        {
            var sentinel = Path.Combine(root, "sentinel"); await File.WriteAllTextAsync(sentinel, "keep");
            using var first = Profile.Acquire(new() { Directory = root });
            Assert.Throws<BrowserDockException>(() => Profile.Acquire(new() { Directory = root }));
            await first.CleanupAsync(true, default);
            Assert.That(await File.ReadAllTextAsync(sentinel), Is.EqualTo("keep"));
            using var again = Profile.Acquire(new() { Directory = root });
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public void RejectsReparsePointOwnedPaths()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Symlink creation requires an independently configured Windows privilege.");
        var root = TestPaths.NewDirectory();
        try { Directory.CreateSymbolicLink(Path.Combine(root, "link"), root); Assert.Throws<BrowserDockException>(() => Paths.Canonical(Path.Combine(root, "link", "profile"))); }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public async Task TemporaryProfileRequiresMatchingMarkerBeforeDeletion()
    {
        using var profile = Profile.Acquire(new());
        var marker = Path.Combine(profile.Path, ".browserdock-owner.json");
        var original = await File.ReadAllTextAsync(marker);
        await File.WriteAllTextAsync(marker, "{\"Schema\":99,\"Nonce\":\"wrong\",\"Path\":\"wrong\"}");
        Assert.ThrowsAsync<BrowserDockException>(async () => await profile.CleanupAsync(true, default));
        Assert.That(Directory.Exists(profile.Path), Is.True);
        await File.WriteAllTextAsync(marker, original);
        await profile.CleanupAsync(true, default);
        Assert.That(Directory.Exists(profile.Path), Is.False);
    }
    [Test]
    public void LiveBrowserPreventsProfileDeletionAndUnlock()
    {
        var root = TestPaths.NewDirectory();
        try
        {
            using var profile = Profile.Acquire(new() { Directory = root });
            Assert.ThrowsAsync<BrowserDockException>(async () => await profile.CleanupAsync(false, default));
            Assert.Throws<BrowserDockException>(() => Profile.Acquire(new() { Directory = root }));
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public void SourceDoesNotDependOnSeleniumPrivateApisOrNameBasedKill()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrowserDock.slnx"))) directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var files = Directory.GetFiles(Path.Combine(directory!.FullName, "src", "BrowserDock"), "*.cs", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var forbidden in new[] { "BindingFlags.NonPublic", "System.Reflection", "Thread.Sleep", "taskkill", "GetProcessesByName", ".StartSession(" })
                Assert.That(source, Does.Not.Contain(forbidden), Path.GetFileName(file));
        }
    }
    [TestCase("a b", "\"a b\"")]
    [TestCase("a\"b", "\"a\\\"b\"")]
    [TestCase("C:\\folder\\", "\"C:\\folder\\\\\"")]
    public void NativeLaunchQuotesArguments(string input, string expected) => Assert.That(WindowsJob.Quote(input), Is.EqualTo(expected));
}
internal static class TestPaths
{
    public static string NewDirectory()
    {
        var root = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        var path = Path.Combine(root, "browserdock-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
}
