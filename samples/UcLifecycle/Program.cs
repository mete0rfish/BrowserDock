using BrowserDock;

if (args.Length is not (3 or 4) || (args.Length == 4 && args[3] != "--profile"))
{
    Console.Error.WriteLine("Usage: UcLifecycle <chrome.exe> <chromedriver.exe> <URL> [--profile]");
    return 2;
}
await using var browser = await Browser.StartAsync(new()
{
    ChromeBinaryPath = args[0], Driver = new() { ExecutablePath = args[1] },
    UcProfile = args.Length == 4 ? new() { Language = "en-US" } : null
});
if (browser.UcProfile is { } profile) Console.WriteLine($"{profile.Id}; language={profile.Language}; patch={profile.PatchMode}");
var uc = browser.Uc;
var result = await uc.OpenWithDisconnectAsync(new Uri(args[2]), TimeSpan.FromSeconds(1));
Console.WriteLine($"{result.Outcome}: {result.FinalUrl}; {browser.State}");
await using var first = await uc.ConnectAsync();
Console.WriteLine(await first.Commands.GetTitleAsync());
// Reconnect also accepts an existing attachment and invalidates first.
await using var fresh = await uc.ReconnectAsync(TimeSpan.FromSeconds(1));
Console.WriteLine($"New session: {fresh.Generation}; {await fresh.Commands.GetUrlAsync()}");
return 0;
