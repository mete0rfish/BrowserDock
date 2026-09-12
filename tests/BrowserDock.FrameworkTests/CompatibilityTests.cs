using System.Diagnostics;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.FrameworkTests;

[TestFixture]
public sealed class CompatibilityTests
{
    [TestCase("", "\"\"")]
    [TestCase("a b", "\"a b\"")]
    [TestCase("a\"b", "\"a\\\"b\"")]
    [TestCase("C:\\folder\\", "\"C:\\folder\\\\\"")]
    public void FrameworkProcessArgumentsUseWindowsEscaping(string argument, string expected)
        => Assert.That(Hosting.WindowsJob.Quote(argument), Is.EqualTo(expected));

    [Test]
    public async Task CompletedOperationWinsOverCancellationAndFaultsKeepTheirIdentity()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.That(await TaskCompatibility.WaitAsync(Task.FromResult(42), canceled.Token), Is.EqualTo(42));
        var expected = new InvalidOperationException("original");
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await TaskCompatibility.WaitAsync(Task.FromException(expected), canceled.Token));
        Assert.That(error, Is.SameAs(expected));
    }

    [Test]
    public async Task CancellationAndTimeoutLeaveUnderlyingWorkOwnedByTheCaller()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = new CancellationTokenSource();
        var pending = TaskCompatibility.WaitAsync(source.Task, canceled.Token);
        canceled.Cancel();
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(error!.CancellationToken, Is.EqualTo(canceled.Token));
        Assert.ThrowsAsync<TimeoutException>(async () => await TaskCompatibility.WaitAsync(source.Task, TimeSpan.FromMilliseconds(30)));
        Assert.That(source.Task.IsCompleted, Is.False);
        source.SetResult(7);
        Assert.That(await source.Task, Is.EqualTo(7));
    }

    [Test]
    public async Task WaitAndFacadeDoNotPostContinuationsToUiContext()
    {
        var context = new RecordingContext();
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> pending;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = Legacy.Api.Call(() => TaskCompatibility.WaitAsync(source.Task, TimeSpan.FromSeconds(3)));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        source.SetResult(11);
        Assert.That(await TaskCompatibility.WaitAsync(pending, TimeSpan.FromSeconds(3)), Is.EqualTo(11));
        Assert.That(context.Posts, Is.Zero);
    }

    [Test]
    public async Task ProcessWaitHandlesExitBeforeSubscription()
    {
        var info = new ProcessStartInfo(Platform.IsWindows ? "cmd.exe" : "/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        RuntimeCompatibility.SetArguments(info, Platform.IsWindows ? new[] { "/c", "exit", "0" } : new[] { "-c", "exit 0" });
        using var process = Process.Start(info)!;
        await TaskCompatibility.WaitAsync(TaskCompatibility.WaitForExitAsync(process), TimeSpan.FromSeconds(5));
        await TaskCompatibility.WaitForExitAsync(process);
        Assert.That(process.ExitCode, Is.Zero);
    }

    [Test]
    public async Task CancelingProcessWaitDoesNotKillProcessOrBreakTheNextWait()
    {
        using var server = await FixtureServer.StartAsync();
        using var canceled = new CancellationTokenSource();
        var pending = TaskCompatibility.WaitForExitAsync(server.Process, canceled.Token);
        canceled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(server.Process.HasExited, Is.False);
        var next = TaskCompatibility.WaitForExitAsync(server.Process);
        server.Process.Kill();
        await TaskCompatibility.WaitAsync(next, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void LegacyApiDoesNotExposeModernModelsOrAsyncDisposalInterfaces()
    {
        foreach (var type in typeof(Legacy.Browser).Assembly.GetExportedTypes())
        {
            Assert.That(type.GetInterfaces().Any(x => x.FullName == "System.IAsyncDisposable"), Is.False, type.FullName);
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            {
                CheckType(method.ReturnType);
                foreach (var parameter in method.GetParameters()) CheckType(parameter.ParameterType);
            }
        }
        static void CheckType(Type type)
        {
            Assert.That(type.Namespace, Is.Not.EqualTo("BrowserDock"), type.FullName);
            Assert.That(type.FullName ?? "", Does.Not.StartWith("System.Threading.Tasks.ValueTask").And.Not.StartWith("OpenQA.Selenium"));
            foreach (var argument in type.GetGenericArguments()) CheckType(argument);
        }
    }

    [Test]
    public void OptionsSnapshotCopiesNestedCollectionsAndRecipeBytes()
    {
        var pattern = new Legacy.PatchPattern { Id = "p", Search = new byte[] { 1 }, Replacement = new byte[] { 2 }, ExpectedCount = 1 };
        var recipe = new Legacy.DriverPatchRecipe { RecipeId = "r", MinimumVersion = new Version(1, 0), MaximumVersion = new Version(2, 0), Patterns = new[] { pattern } };
        var options = new Legacy.BrowserOptions { ChromeArguments = new List<string> { "--lang=en" }, Driver = new Legacy.DriverArtifactOptions { ExecutablePath = "driver", PatchRecipes = new[] { recipe } } };
        options.Profile.Directory = "before";
        var snapshot = options.Snapshot();
        options.ChromeArguments[0] = "--lang=ko"; options.Profile.Directory = "after";
        options.Timeouts.ChromeStart = TimeSpan.FromSeconds(1); pattern.Search[0] = 9; recipe.RecipeId = "changed";
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ChromeArguments.Single(), Is.EqualTo("--lang=en"));
            Assert.That(snapshot.Profile.Directory, Is.EqualTo("before"));
            Assert.That(snapshot.Timeouts.ChromeStart, Is.Not.EqualTo(options.Timeouts.ChromeStart));
            Assert.That(snapshot.Driver.PatchStrategies.Single().RecipeId, Is.EqualTo("r"));
            Assert.That(snapshot.Driver.PatchStrategies.Single().Patterns.Single().Search[0], Is.EqualTo(1));
        });
    }

    [Test]
    public void ScriptSnapshotCopiesNestedValuesAndRejectsCycles()
    {
        var array = new object[] { "before" };
        var input = new Dictionary<string, object> { ["values"] = array };
        var copy = (Dictionary<string, object?>)Legacy.GuardedCommands.SnapshotValue(input)!;
        array[0] = "after";
        Assert.That(((object?[])copy["values"]!)[0], Is.EqualTo("before"));
        input["cycle"] = input;
        Assert.Throws<ArgumentException>(() => Legacy.GuardedCommands.SnapshotValue(input));
    }

    [Test]
    public void FacadePreservesTypedErrorsAndCancellationMetadata()
    {
        var original = new StaleAttachmentException { OperationId = Guid.NewGuid(), BrowserMayHaveAdvanced = true, CleanupFailures = new[] { "cleanup" } };
        var error = Assert.ThrowsAsync<Legacy.StaleAttachmentException>(async () => await Legacy.Api.Call(() => Task.FromException(original)));
        Assert.Multiple(() =>
        {
            Assert.That(error!.OperationId, Is.EqualTo(original.OperationId));
            Assert.That(error.BrowserMayHaveAdvanced, Is.True);
            Assert.That(error.CleanupFailures.Single(), Is.EqualTo("cleanup"));
            Assert.That(error.InnerException, Is.SameAs(original));
        });
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var navigation = Assert.ThrowsAsync<Legacy.NavigationCanceledException>(async () => await Legacy.Api.Call(() => Task.FromException(new NavigationCanceledException(canceled.Token, true))));
        Assert.That(navigation!.CancellationToken, Is.EqualTo(canceled.Token));
        Assert.That(navigation.BrowserMayHaveAdvanced, Is.True);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state) { Interlocked.Increment(ref Posts); ThreadPool.QueueUserWorkItem(_ => callback(state)); }
    }
}
