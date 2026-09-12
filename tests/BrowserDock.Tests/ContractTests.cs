using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using BrowserDock.Cdp;
using BrowserDock.WebDriver;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ContractTests
{
    [Test]
    public async Task PublicSeleniumConstructorAndDisposeNeverSendDeleteSession()
    {
        var requests = new ConcurrentQueue<(string Method, string Path, string Body)>();
        await using var server = await LocalServer.StartAsync(async context =>
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            requests.Enqueue((context.Request.Method, context.Request.Path, body));
            if (context.Request.Path == "/session") await context.Response.WriteAsJsonAsync(new { value = new { sessionId = "fixture-session", capabilities = new { browserName = "chrome", browserVersion = "150.0.1.0", platformName = "windows" } } });
            else await context.Response.WriteAsJsonAsync(new { value = "http://localhost/fixture" });
        });
        var executor = new DetachAwareCommandExecutor(new LoopbackExecutor(server.Url, TimeSpan.FromSeconds(3)));
        var options = new ChromeOptions { DebuggerAddress = "127.0.0.1:12345", LeaveBrowserRunning = true };
        var driver = await Task.Run(() => new RemoteWebDriver(executor, options.ToCapabilities()));
        Assert.That(driver.Url, Is.EqualTo("http://localhost/fixture"));
        var creation = requests.First();
        using var json = JsonDocument.Parse(creation.Body);
        var capabilities = json.RootElement.GetProperty("capabilities");
        var chrome = capabilities.TryGetProperty("alwaysMatch", out var always) && always.TryGetProperty("goog:chromeOptions", out var chromeOptions)
            ? chromeOptions : capabilities.GetProperty("firstMatch").EnumerateArray().Single(x => x.TryGetProperty("goog:chromeOptions", out _)).GetProperty("goog:chromeOptions");
        Assert.Multiple(() => { Assert.That(chrome.GetProperty("debuggerAddress").GetString(), Is.EqualTo("127.0.0.1:12345")); Assert.That(chrome.GetProperty("detach").GetBoolean(), Is.True); });
        var count = requests.Count;
        executor.Detach();
        Assert.Throws<StaleAttachmentException>(() => _ = driver.Url);
        driver.Dispose(); executor.Dispose();
        Assert.That(requests.Count, Is.EqualTo(count));
        Assert.That(requests.Any(x => x.Method == "DELETE"), Is.False);
    }
    [Test]
    public async Task CdpMultiplexesOutOfOrderResponsesAndIncludesPageSession()
    {
        var seen = new ConcurrentQueue<JsonElement>();
        await using var server = await LocalServer.StartAsync(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var requests = new List<JsonElement>();
            for (var i = 0; i < 2; i++) { requests.Add(await Receive(socket)); seen.Enqueue(requests[^1]); }
            foreach (var request in requests.AsEnumerable().Reverse())
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id = request.GetProperty("id").GetInt64(), result = new { echoed = request.GetProperty("method").GetString() } });
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, default);
            }
            try { await Receive(socket); } catch (WebSocketException) { }
        });
        await using var connection = new CdpConnection();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(new UriBuilder(server.Url) { Scheme = "ws" }.Uri, deadline.Token);
        var first = connection.SendAsync("Runtime.evaluate", new { expression = "1" }, "page-session", deadline.Token);
        var second = connection.SendAsync("Browser.getVersion", null, null, deadline.Token);
        Assert.That((await first).GetProperty("echoed").GetString(), Is.EqualTo("Runtime.evaluate"));
        Assert.That((await second).GetProperty("echoed").GetString(), Is.EqualTo("Browser.getVersion"));
        Assert.That(seen.First().GetProperty("sessionId").GetString(), Is.EqualTo("page-session"));
        Assert.That(seen.Last().TryGetProperty("sessionId", out _), Is.False);
    }
    [Test]
    public async Task CdpSocketLossFailsPendingRequestsWithoutHanging()
    {
        await using var server = await LocalServer.StartAsync(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync(); await Receive(socket); socket.Abort();
        });
        await using var connection = new CdpConnection();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(new UriBuilder(server.Url) { Scheme = "ws" }.Uri, deadline.Token);
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await connection.SendAsync("Browser.getVersion", null, null, deadline.Token));
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.DevToolsEndpointFailure));
    }
    private static async Task<JsonElement> Receive(WebSocket socket)
    {
        var bytes = new byte[8192]; using var stream = new MemoryStream();
        WebSocketReceiveResult received;
        do { received = await socket.ReceiveAsync(bytes, CancellationToken.None); if (received.MessageType == WebSocketMessageType.Close) throw new WebSocketException(); stream.Write(bytes, 0, received.Count); } while (!received.EndOfMessage);
        using var json = JsonDocument.Parse(stream.ToArray()); return json.RootElement.Clone();
    }
}
internal sealed class LocalServer(WebApplication app, Uri url) : IAsyncDisposable
{
    public Uri Url { get; } = url;
    public static async Task<LocalServer> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        var app = builder.Build(); app.UseWebSockets(); app.Run(handler); await app.StartAsync();
        return new(app, new Uri(app.Urls.Single()));
    }
    public async ValueTask DisposeAsync() { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await app.StopAsync(deadline.Token); await app.DisposeAsync(); }
}
