using System.Text.Json;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using BrowserDock.Cdp;
using BrowserDock.WebDriver;

namespace BrowserDock.FrameworkTests;

[TestFixture]
public sealed class ProtocolTests
{
    [Test]
    public async Task ExternalFixtureHostServesTheSharedReferencePage()
    {
        using var server = await FixtureServer.StartAsync();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        var page = await RuntimeCompatibility.HttpStringAsync(http, new Uri(server.Url, "/reference/page").AbsoluteUri, CancellationToken.None);
        Assert.That(page, Does.Contain("<title>BrowserDock reference</title>"));
        using var redirect = await http.GetAsync(new Uri(server.Url, "/reference/redirect"));
        Assert.That((int)redirect.StatusCode, Is.EqualTo(302));
        Assert.That(redirect.Headers.Location!.OriginalString, Is.EqualTo("/reference/page"));
        using var missing = await http.GetAsync(new Uri(server.Url, "/reference/missing"));
        Assert.That((int)missing.StatusCode, Is.EqualTo(404));
    }
    [Test]
    public async Task SeleniumPublicConstructorFindAndDetachKeepWireContract()
    {
        using var server = await FixtureServer.StartAsync();
        using var executor = new DetachAwareCommandExecutor(new LoopbackExecutor(new Uri(server.Url, "/wd/"), TimeSpan.FromSeconds(5)));
        var options = new ChromeOptions { DebuggerAddress = "127.0.0.1:12345", LeaveBrowserRunning = true };
        using var driver = await Task.Run(() => new RemoteWebDriver(executor, options.ToCapabilities()));
        Assert.That(driver.FindElement(By.Id("button")).Text, Is.EqualTo("fixture-value"));
        executor.Detach();
        Assert.Throws<StaleAttachmentException>(() => _ = driver.Url);
        driver.Dispose();
        var requests = await Requests(server);
        Assert.That(requests.Any(x => x.GetProperty("method").GetString() == "DELETE"), Is.False);
        Assert.That(requests.Any(x => x.GetProperty("path").GetString()!.EndsWith("/element/element-fixture/text", StringComparison.Ordinal)), Is.True);
        using var creation = JsonDocument.Parse(requests[0].GetProperty("body").GetString()!);
        Assert.That(creation.RootElement.ToString(), Does.Contain("127.0.0.1:12345").And.Contain("\"detach\":true"));
    }

    [Test]
    public async Task CdpRoutesOutOfOrderRepliesAndSessionIds()
    {
        using var server = await FixtureServer.StartAsync();
        await using var connection = new CdpConnection();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(Endpoint(server, "reverse"), deadline.Token);
        var first = connection.SendAsync("Runtime.evaluate", new { expression = "1" }, "page-session", deadline.Token);
        var second = connection.SendAsync("Browser.getVersion", null, null, deadline.Token);
        Assert.That((await first).GetProperty("echoed").GetString(), Is.EqualTo("Runtime.evaluate"));
        Assert.That((await second).GetProperty("echoed").GetString(), Is.EqualTo("Browser.getVersion"));
        var requests = await Requests(server);
        Assert.That(requests[0].GetProperty("payload").GetProperty("sessionId").GetString(), Is.EqualTo("page-session"));
        Assert.That(requests[1].GetProperty("payload").TryGetProperty("sessionId", out _), Is.False);
    }

    [TestCase("hold"), TestCase("drop")]
    public async Task CdpCancellationAndSocketLossReleasePendingRequests(string scenario)
    {
        using var server = await FixtureServer.StartAsync();
        await using var connection = new CdpConnection();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(Endpoint(server, scenario), deadline.Token);
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        if (scenario == "hold")
            Assert.CatchAsync<OperationCanceledException>(async () => await connection.SendAsync("Browser.getVersion", null, null, canceled.Token));
        else
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await connection.SendAsync("Browser.getVersion", null, null, deadline.Token))!.Category, Is.EqualTo(ErrorCategory.DevToolsEndpointFailure));
    }

    [TestCase(NavigationWaitUntil.Commit), TestCase(NavigationWaitUntil.DOMContentLoaded), TestCase(NavigationWaitUntil.Load), TestCase(NavigationWaitUntil.NetworkIdle)]
    public async Task NavigationAndRecoveryPreserveTargetAndInstallScripts(NavigationWaitUntil until)
    {
        using var server = await FixtureServer.StartAsync();
        await using var controller = new CdpController(Endpoint(server), new[] { "globalThis.fixture=true" });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await controller.InitializeAsync(deadline.Token);
        var original = controller.Controlled;
        var result = await controller.NavigateAsync(new Uri("http://fixture/page"), new() { WaitUntil = until }, null, deadline.Token);
        Assert.That(result.Url.AbsoluteUri, Is.EqualTo("http://fixture/page"));
        controller.InterruptForTest();
        await controller.RecoverAsync(deadline.Token);
        Assert.That(controller.Controlled, Is.EqualTo(original));
        var requests = await Requests(server);
        Assert.That(requests.Count(x => x.GetProperty("method").GetString() == "Page.addScriptToEvaluateOnNewDocument"), Is.EqualTo(2));
        Assert.That(requests.Where(x => x.GetProperty("method").GetString()!.StartsWith("Page.", StringComparison.Ordinal)).All(x => x.GetProperty("payload").GetProperty("sessionId").GetString() == "page-session"), Is.True);
    }

    private static Uri Endpoint(FixtureServer server, string scenario = "") => new UriBuilder(server.Url) { Scheme = "ws", Path = "/cdp", Query = "scenario=" + scenario }.Uri;
    private static async Task<JsonElement[]> Requests(FixtureServer server)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var document = JsonDocument.Parse(await RuntimeCompatibility.HttpStringAsync(http, new Uri(server.Url, "/control/requests").AbsoluteUri, CancellationToken.None));
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
