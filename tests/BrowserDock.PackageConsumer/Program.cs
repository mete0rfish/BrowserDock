using BrowserDock;

// Compile and load public package APIs without starting a browser.
Console.WriteLine(typeof(Browser).Assembly.FullName);
Console.WriteLine(typeof(BrowserOptions).Assembly.FullName);
Console.WriteLine(typeof(BrowserDock.Legacy.Browser).Assembly.FullName);
