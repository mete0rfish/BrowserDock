using System;
using System.Threading.Tasks;
using BrowserDock.Legacy;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if ((args.Length != 3 && args.Length != 4) || (args.Length == 4 && args[3] != "--profile"))
        {
            Console.Error.WriteLine("Usage: UcFramework481.exe <chrome.exe> <chromedriver.exe> <URL> [--profile]");
            return 2;
        }
        var browser = await Browser.StartAsync(new BrowserOptions
        {
            ChromeBinaryPath = args[0], Driver = new DriverArtifactOptions { ExecutablePath = args[1] },
            UcProfile = args.Length == 4 ? new UcLaunchProfile { Language = "en-US" } : null
        });
        try
        {
            if (browser.UcProfile != null) Console.WriteLine(browser.UcProfile.Id + "; language=" + browser.UcProfile.Language + "; patch=" + browser.UcProfile.PatchMode);
            var uc = browser.Uc;
            var result = await uc.OpenWithDisconnectAsync(new Uri(args[2]), TimeSpan.FromSeconds(1));
            Console.WriteLine(result.FinalUrl + "; " + browser.State);
            var first = await uc.ConnectAsync();
            try
            {
                Console.WriteLine(await first.Commands.GetTitleAsync());
                var fresh = await uc.ReconnectAsync(TimeSpan.FromSeconds(1));
                try { Console.WriteLine("New session: " + fresh.Generation); }
                finally { await fresh.DisposeAsync(); }
            }
            finally { await first.DisposeAsync(); }
            return 0;
        }
        finally { await browser.DisposeAsync(); }
    }
}
