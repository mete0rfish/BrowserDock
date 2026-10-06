using System.Text.Json;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class UcLaunchProfileTests
{
    private static BrowserOptions Options(UcLaunchProfile? profile = null) => new()
    { Driver = new() { ExecutablePath = "not-started" }, UcProfile = profile };

    [Test]
    public void OrdinaryDefaultsDoNotEnableProfile()
    {
        var captured = Browser.CaptureOptions(Options());
        Assert.Multiple(() =>
        {
            Assert.That(captured.UcProfile, Is.Null);
            Assert.That(captured.RemoveDiscoveredCdcProperties, Is.False);
            Assert.That(captured.ChromeArguments, Is.Empty);
            Assert.That(UcProfileResolver.Describe(captured), Is.Null);
        });
    }

    [TestCase(false), TestCase(true)]
    public void ProfileAndCallerScriptsAreCapturedOnceInStableOrder(bool legacy)
    {
        var profileScripts = new List<string> { "globalThis.order=['profile']", "globalThis.order.push('second')" };
        var callerScripts = new List<string> { profileScripts[1], "globalThis.order.push('caller')" };
        var arguments = new List<string> { "--disable-extensions", "--lang=ko-KR", "--LANG=ko-KR" };
        var legacyProfile = new Legacy.UcLaunchProfile { Language = "ko-KR", NewDocumentScripts = profileScripts };
        var source = legacy
            ? new Legacy.BrowserOptions { Driver = new() { ExecutablePath = "not-started" }, UcProfile = legacyProfile,
                ChromeArguments = arguments, NewDocumentScripts = callerScripts }.Snapshot()
            : Options(new() { Language = "ko-KR", NewDocumentScripts = profileScripts }) with { ChromeArguments = arguments, NewDocumentScripts = callerScripts };
        var captured = Browser.CaptureOptions(source);
        profileScripts.Clear(); callerScripts.Clear(); arguments.Clear(); legacyProfile.Language = "fr-FR";
        Assert.Multiple(() =>
        {
            Assert.That(captured.ChromeArguments, Is.EqualTo(new[] { "--disable-extensions", "--lang=ko-KR" }));
            Assert.That(captured.NewDocumentScripts, Is.EqualTo(new[] { "globalThis.order=['profile']", "globalThis.order.push('second')", "globalThis.order.push('caller')" }));
            Assert.That(captured.UcProfile!.Language, Is.EqualTo("ko-KR"));
            Assert.That(captured.UcProfile.NewDocumentScripts.Count, Is.EqualTo(2));
            Assert.That(captured.RemoveDiscoveredCdcProperties, Is.True);
            Assert.That(captured.PatchMode, Is.EqualTo(DriverPatchMode.Disabled));
        });
        Assert.Throws<NotSupportedException>(() => ((IList<string>)captured.UcProfile!.NewDocumentScripts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)captured.NewDocumentScripts).Clear());
        var second = Browser.CaptureOptions(captured);
        Assert.That(second.NewDocumentScripts, Is.EqualTo(captured.NewDocumentScripts));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void ExplicitCdcOverrideCannotBeSilentlyReplaced(bool legacy, bool profileValue)
    {
        var source = legacy
            ? new Legacy.BrowserOptions { Driver = new() { ExecutablePath = "unused" }, RemoveDiscoveredCdcProperties = !profileValue,
                UcProfile = new() { RemoveDiscoveredCdcProperties = profileValue } }.Snapshot()
            : Options(new() { RemoveDiscoveredCdcProperties = profileValue }) with { RemoveDiscoveredCdcProperties = !profileValue };
        Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(source))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
        var matched = Browser.CaptureOptions(source with { RemoveDiscoveredCdcProperties = profileValue });
        Assert.That(matched.RemoveDiscoveredCdcProperties, Is.EqualTo(profileValue));
    }

    [TestCase(false), TestCase(true)]
    public void LegacySnapshotRetainsAbsenceOfAnOverride(bool enabled)
    {
        var source = new Legacy.BrowserOptions { UcProfile = new() { RemoveDiscoveredCdcProperties = enabled } }.Snapshot();
        Assert.That(source.CdcPropertyRemovalOverride, Is.Null);
        Assert.That(Browser.CaptureOptions(source).RemoveDiscoveredCdcProperties, Is.EqualTo(enabled));
    }

    [TestCase(null), TestCase(""), TestCase(" en-US"), TestCase("en-US,en;q=0.9"), TestCase("en-US\n--enable-automation")]
    public void InvalidLanguagesFailDuringCapture(string? language)
        => Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(Options(new() { Language = language! })))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));

    [TestCase("--lang=fr-FR"), TestCase("--lang"), TestCase("--lang en-US"), TestCase("--enable-automation"), TestCase("--ENABLE-AUTOMATION=false"), TestCase("--remote-debugging-port=9222"), TestCase("--headless=new"), TestCase("--profile-directory=Default")]
    public void IncompatibleArgumentsFailDuringCapture(string argument)
        => Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(Options(new()) with { ChromeArguments = new[] { argument } }))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));

    [TestCase(0), TestCase(2)]
    public void UnknownVersionsAreRejected(int version)
        => Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(Options(new() { Version = version })))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));

    [TestCase(DriverPatchMode.Disabled), TestCase(DriverPatchMode.ValidateOnly), TestCase(DriverPatchMode.BinaryCompatibility)]
    public void RequiredPatchCannotFallBackOrInventARecipe(DriverPatchMode mode)
        => Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(Options(new() { RequireBinaryPatch = true }) with { PatchMode = mode }))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));

    [Test]
    public void RequiredPatchKeepsTheExplicitRecipeForLaterVersionValidation()
    {
        var recipe = new Legacy.DriverPatchRecipe { RecipeId = "synthetic-only", MinimumVersion = new Version(150, 0), MaximumVersion = new Version(150, 99) };
        var source = new Legacy.BrowserOptions
        {
            Driver = new() { ExecutablePath = "not-started", PatchRecipes = new[] { recipe } },
            PatchMode = Legacy.DriverPatchMode.BinaryCompatibility, UcProfile = new() { RequireBinaryPatch = true }
        }.Snapshot();
        var captured = Browser.CaptureOptions(source);
        var strategy = captured.Driver.PatchStrategies.Single();
        Assert.That(strategy.RecipeId, Is.EqualTo("synthetic-only"));
        Assert.That(strategy.Supports(new Version(150, 1)), Is.True);
        Assert.That(strategy.Supports(new Version(154, 0)), Is.False);
        Assert.That(UcProfileResolver.Describe(captured)!.RequireBinaryPatch, Is.True);
        // Capture validates configuration, not the vendor executable. No file/process was needed.
    }

    [TestCase(false), TestCase(true)]
    public void NullNestedScriptListHasTheSameConfigurationErrorInBothFacades(bool legacy)
    {
        var source = legacy ? new Legacy.BrowserOptions { UcProfile = new() { NewDocumentScripts = null! } }.Snapshot()
            : Options(new() { NewDocumentScripts = null! });
        Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(source))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
    }

    [TestCase(null), TestCase(""), TestCase(" ")]
    public void EmptyProfileScriptsAreRejected(string? script)
        => Assert.That(Assert.Throws<BrowserDockException>(() => Browser.CaptureOptions(Options(new() { NewDocumentScripts = new[] { script! } })))!.Category, Is.EqualTo(ErrorCategory.ConfigurationError));

    [Test]
    public void DiagnosticsExposePolicyWithoutCallerContent()
    {
        var captured = Browser.CaptureOptions(Options(new() { NewDocumentScripts = new[] { "globalThis.secret='private-script'" } }) with
        { ChromeArguments = new[] { "--private-argument=secret-value" }, Profile = new() { Directory = "private-path" } });
        var info = UcProfileResolver.Describe(captured)!;
        var legacy = new Legacy.UcLaunchProfileInfo(info);
        Assert.That(JsonSerializer.Serialize(legacy), Is.EqualTo(JsonSerializer.Serialize(info)));
        var json = JsonSerializer.Serialize(info);
        Assert.Multiple(() =>
        {
            Assert.That(info.Id, Is.EqualTo("browserdock-uc-v1"));
            Assert.That(info.DocumentScriptCount, Is.EqualTo(1));
            Assert.That(json, Does.Not.Contain("private").And.Not.Contain("secret"));
        });
    }
}
