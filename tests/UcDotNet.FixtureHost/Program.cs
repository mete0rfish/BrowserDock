using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
var app = builder.Build();
app.UseWebSockets();
var requests = new ConcurrentQueue<object>();
app.MapGet("/control/requests", () => requests.ToArray());
app.MapPost("/control/reset", () => { requests.Clear(); return Results.Ok(); });
app.MapPost("/control/stop", (IHostApplicationLifetime lifetime) => { lifetime.StopApplication(); return Results.Ok(); });
app.MapGet("/json/protocol", () => new
{
    domains = new Dictionary<string, string[]>
    {
        ["Browser"] = ["getVersion", "close", "setDownloadBehavior"],
        ["Target"] = ["setDiscoverTargets", "getTargets", "attachToTarget", "activateTarget", "createTarget", "closeTarget"],
        ["Page"] = ["enable", "navigate", "getFrameTree", "setLifecycleEventsEnabled", "addScriptToEvaluateOnNewDocument"],
        ["Runtime"] = ["enable", "evaluate", "callFunctionOn"], ["Network"] = ["enable"]
    }.Select(pair => new { domain = pair.Key, commands = pair.Value.Select(name => new { name }) })
});
app.Map("/cdp", async context =>
{
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[16384];
    var deferred = new List<(long Id, string Method)>();
    var scenario = context.Request.Query["scenario"].ToString();
    try
    {
        while (!context.RequestAborted.IsCancellationRequested)
        {
            using var stream = new MemoryStream(); WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (received.MessageType == WebSocketMessageType.Close) return;
                stream.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            using var json = JsonDocument.Parse(stream.ToArray());
            var root = json.RootElement;
            var id = root.GetProperty("id").GetInt64(); var method = root.GetProperty("method").GetString()!;
            requests.Enqueue(new { method, payload = root.Clone() });
            if (scenario == "drop") { socket.Abort(); return; }
            if (scenario == "hold") continue;
            if (scenario == "reverse")
            {
                deferred.Add((id, method));
                if (deferred.Count == 2) { foreach (var entry in deferred.AsEnumerable().Reverse()) await Send(new { id = entry.Id, result = new { echoed = entry.Method } }); deferred.Clear(); }
                continue;
            }
            object result = method switch
            {
                "Browser.getVersion" => new { product = "Chrome/150.0.1.0", protocolVersion = "1.3" },
                "Target.getTargets" => new { targetInfos = new[] { new { targetId = "page", type = "page", url = "about:blank", title = "fixture" } } },
                "Target.attachToTarget" => new { sessionId = "page-session" },
                "Page.getFrameTree" => new { frameTree = new { frame = new { id = "main", loaderId = "old" } } },
                "Page.navigate" => new { frameId = "main", loaderId = "new" },
                "Runtime.evaluate" => new { result = new { type = "string", value = "http://fixture/page" } },
                _ => new { }
            };
            await Send(new { id, result });
            if (method == "Page.navigate")
            {
                await Send(new { method = "Page.frameNavigated", sessionId = "page-session", @params = new { frame = new { id = "main", loaderId = "new", url = "http://fixture/page" } } });
                foreach (var name in new[] { "DOMContentLoaded", "load" }) await Send(new { method = "Page.lifecycleEvent", sessionId = "page-session", @params = new { frameId = "main", loaderId = "new", name } });
            }
        }
    }
    catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
    async Task Send(object payload) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, true, context.RequestAborted);
});
app.Map("/wd/{**path}", async context =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    requests.Enqueue(new { method = context.Request.Method, path = context.Request.Path.Value, body });
    object response = context.Request.Path.Value switch
    {
        "/wd/session" => new { value = new { sessionId = "fixture-session", capabilities = new { browserName = "chrome", browserVersion = "150.0.1.0", platformName = "windows" } } },
        "/wd/session/fixture-session/element" => new { value = new Dictionary<string, string> { ["element-6066-11e4-a52e-4f735466cecf"] = "element-fixture" } },
        _ => new { value = "fixture-value" }
    };
    await context.Response.WriteAsJsonAsync(response);
});
app.MapGet("/redirect", () => Results.Redirect("/page"));
app.MapGet("/download", () => Results.File("fixture"u8.ToArray(), "application/octet-stream", "fixture.txt"));
app.MapGet("/slow", async (HttpContext context) => { await Task.Delay(10000, context.RequestAborted); return Results.Content("<title>slow</title>", "text/html"); });
app.MapGet("/frame", () => Results.Content("<input id=frame-input>", "text/html"));
app.MapGet("/page", () => Results.Content("""
    <!doctype html><title>Framework fixture</title>
    <button id="button" onclick="this.textContent='clicked'">ready</button><input id="input"><iframe src="/frame"></iframe>
    """, "text/html"));
await app.StartAsync();
Console.WriteLine("UCDOTNET_FIXTURE_URL=" + app.Urls.Single());
await app.WaitForShutdownAsync();
