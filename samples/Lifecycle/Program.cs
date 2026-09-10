using UcDotNet;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Lifecycle <chrome.exe> <chromedriver.exe> <http(s) URL>");
    return 2;
}
await using var browser = await UcBrowser.StartAsync(new BrowserOptions
{
    ChromeBinaryPath = args[0], Driver = new() { ExecutablePath = args[1] }
});
await browser.NavigateAsync(new Uri(args[2]));
await using var original = await browser.GetWebDriverAsync();
Console.WriteLine(await original.Commands.GetTitleAsync());
await browser.DisconnectWebDriverAsync();
Console.WriteLine($"Disconnected: {browser.State}; Chrome PID: {browser.Health.ChromePid}");
await using var fresh = await browser.ReconnectWebDriverAsync();
Console.WriteLine($"New session generation: {fresh.Generation}; URL: {await fresh.Commands.GetUrlAsync()}");
return 0;
