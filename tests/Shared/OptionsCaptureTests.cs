using System.Collections;
using System.Text;
using BrowserDock.Hosting;
using BrowserDock.Patching;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class OptionsCaptureTests
{
    [Test]
    public async Task CapturedCollectionsRemainIndependentAndDrivePatchPreparation()
    {
        var root = Path.Combine(Paths.TempRoot, "BrowserDock-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "driver.bin");
            File.WriteAllText(executable, "prefix ORIGINAL suffix");
            var arguments = new List<string> { "--lang=en-US", "--disable-extensions" };
            var scripts = new List<string> { "globalThis.snapshot=1", "globalThis.snapshot+=1" };
            var schemes = new List<string> { "custom" };
            var recipe = new Recipe();
            var strategies = new List<IDriverPatchStrategy> { recipe };
            var source = new BrowserOptions
            {
                ChromeBinaryPath = "chrome", Driver = new() { ExecutablePath = executable, CacheDirectory = Path.Combine(root, "cache"), PatchStrategies = strategies },
                Profile = new() { Directory = "profile" }, Timeouts = new() { ChromeStart = TimeSpan.FromSeconds(7) },
                PatchMode = DriverPatchMode.BinaryCompatibility, ChromeArguments = arguments, NewDocumentScripts = scripts,
                AdditionalUrlSchemes = schemes, RemoveDiscoveredCdcProperties = true, Logger = NullLogger.Instance
            };
            var captured = Browser.CaptureOptions(source);
            arguments.Clear(); scripts.Clear(); schemes.Clear(); strategies.Clear();
            var patched = await DriverPatchCache.PrepareAsync(captured.Driver, new Version(150, 0), captured.PatchMode, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(captured.ChromeArguments, Is.EqualTo(new[] { "--lang=en-US", "--disable-extensions" }));
                Assert.That(captured.NewDocumentScripts, Is.EqualTo(new[] { "globalThis.snapshot=1", "globalThis.snapshot+=1" }));
                Assert.That(captured.AdditionalUrlSchemes, Is.EqualTo(new[] { "custom" }));
                Assert.That(captured.Driver.PatchStrategies.Single(), Is.SameAs(recipe), "Custom strategies remain caller-owned.");
                Assert.That(captured.Logger, Is.SameAs(source.Logger), "Loggers are not cloned.");
                Assert.That(captured.Profile, Is.SameAs(source.Profile), "Core profile options are immutable records.");
                Assert.That(captured.Timeouts, Is.SameAs(source.Timeouts));
                Assert.That(captured.ChromeBinaryPath, Is.EqualTo(source.ChromeBinaryPath));
                Assert.That(captured.RemoveDiscoveredCdcProperties, Is.True);
                Assert.That(File.ReadAllText(patched), Is.EqualTo("prefix REPLACED suffix"));
                Assert.That(File.ReadAllText(executable), Is.EqualTo("prefix ORIGINAL suffix"));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void CallerArgumentsAreReadOnceAndTheCopyIsValidated()
    {
        var arguments = new SingleReadArguments("--lang=en-US");
        var captured = Browser.CaptureOptions(new() { Driver = new() { ExecutablePath = "driver" }, ChromeArguments = arguments });
        Assert.That(captured.ChromeArguments, Is.EqualTo(new[] { "--lang=en-US" }));
        Assert.That(arguments.Reads, Is.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)captured.ChromeArguments)[0] = "--proxy-server=unexpected");
    }

    [TestCase("--user-data-dir=other"), TestCase("--remote-debugging-port=1234"), TestCase("--proxy-server=other")]
    public void CapturedManagedArgumentsAreRejected(string argument)
    {
        var arguments = new SingleReadArguments(argument);
        var error = Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(new()
        { Driver = new() { ExecutablePath = "driver" }, ChromeArguments = arguments }));
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(arguments.Reads, Is.EqualTo(1));
    }

    private sealed class SingleReadArguments(string argument) : IReadOnlyList<string>
    {
        public int Reads;
        public int Count => 1;
        public string this[int index] => index == 0 ? argument : throw new IndexOutOfRangeException();
        public IEnumerator<string> GetEnumerator()
        {
            if (++Reads != 1) throw new InvalidOperationException("Caller data was read again after capture.");
            yield return argument;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Recipe : IDriverPatchStrategy
    {
        public string RecipeId => "snapshot-contract-v1";
        public bool Supports(Version driverVersion) => driverVersion.Major == 150;
        public IReadOnlyList<PatchPattern> Patterns { get; } = new[]
        { new PatchPattern("literal", Encoding.ASCII.GetBytes("ORIGINAL"), Encoding.ASCII.GetBytes("REPLACED"), 1) };
    }
}
