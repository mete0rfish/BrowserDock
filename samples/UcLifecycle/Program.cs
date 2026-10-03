using BrowserDock;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: UcLifecycle <chrome.exe> <chromedriver.exe> <URL>");
    return 2;
}
await using var browser = await Browser.StartAsync(new()
{
    ChromeBinaryPath = args[0], Driver = new() { ExecutablePath = args[1] }
});
var uc = browser.Uc;
var result = await uc.OpenWithDisconnectAsync(new Uri(args[2]), TimeSpan.FromSeconds(1));
Console.WriteLine($"{result.Outcome}: {result.FinalUrl}; {browser.State}");
await using var first = await uc.ConnectAsync();
Console.WriteLine(await first.Commands.GetTitleAsync());
// Reconnect also accepts an existing attachment and invalidates first.
await using var fresh = await uc.ReconnectAsync(TimeSpan.FromSeconds(1));
Console.WriteLine($"New session: {fresh.Generation}; {await fresh.Commands.GetUrlAsync()}");
return 0;
