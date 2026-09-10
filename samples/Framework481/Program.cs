using System;
using System.Threading.Tasks;
using UcDotNet.Legacy;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: Framework481.exe <chrome.exe> <chromedriver.exe> <URL>");
            return 2;
        }
        UcBrowser browser = null;
        try
        {
            var options = new BrowserOptions
            {
                ChromeBinaryPath = args[0],
                Driver = new DriverArtifactOptions { ExecutablePath = args[1] }
            };
            browser = await UcBrowser.StartAsync(options);
            await browser.NavigateAsync(new Uri(args[2]));
            var first = await browser.GetWebDriverAsync();
            try
            {
                Console.WriteLine(await first.Commands.GetTitleAsync());
                var body = await first.Commands.FindAsync(Locator.Css("body"));
                Console.WriteLine(await body.IsDisplayedAsync());
                await browser.DisconnectWebDriverAsync();
                var next = await browser.ReconnectWebDriverAsync();
                try
                {
                    Console.WriteLine("New session generation: " + next.Generation);
                    var freshBody = await body.ReacquireAsync();
                    Console.WriteLine(await freshBody.IsDisplayedAsync());
                }
                finally { await next.DisposeAsync(); }
            }
            finally { await first.DisposeAsync(); }
            return 0;
        }
        finally { if (browser != null) await browser.DisposeAsync(); }
    }
}
