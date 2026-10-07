using System.Diagnostics;
using System.Text.Json;
using BrowserDock;
using BrowserDock.Cdp;
using BrowserDock.Hosting;
using BrowserDock.TestFixtures;
using BrowserDock.WebDriver;
using OpenQA.Selenium.Remote;

// Explicit local investigation only. This does not extend BrowserDock's
// supported Windows hosting API and never appears in an automatic browser job.
if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_INVESTIGATION") != "linux")
    throw new InvalidOperationException("Use run.py --suite interactions --linux-investigation.");
var report = HighLevelInteractionReference.Report();
var chromePath = Paths.Canonical(Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME")!);
var driverPath = Paths.Canonical(Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER")!);
var output = Paths.Canonical(Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_RESULTS")!);
var profile = Path.Combine(output, "owned-profile"); Directory.CreateDirectory(profile);
var info = new ProcessStartInfo(chromePath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
foreach (var arg in new[] { "--headless=new", "--no-sandbox", "--disable-dev-shm-usage", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "--user-data-dir=" + profile, "about:blank" }) info.ArgumentList.Add(arg);
using var chrome = Process.Start(info)!; var creation = chrome.StartTime;
var stdout = chrome.StandardOutput.ReadToEndAsync(); var stderr = chrome.StandardError.ReadToEndAsync();
OwnedProcess? driverProcess = null; DetachAwareCommandExecutor? executor = null; RemoteWebDriver? driver = null;
CdpController? controller = null; Browser? browser = null; Exception? failure = null;
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = deadline.Token;
    var activePort = Path.Combine(profile, "DevToolsActivePort");
    while (!File.Exists(activePort)) { if (chrome.HasExited) throw new InvalidOperationException("Owned Chrome exited before endpoint readiness."); await Task.Delay(20, token); }
    var lines = await File.ReadAllLinesAsync(activePort, token); var endpoint = new Uri("ws://127.0.0.1:" + lines[0] + lines[1]);
    var port = BrowserHosting.CandidatePort(); driverProcess = new OwnedProcess(driverPath, new[] { "--port=" + port });
    using var http = BrowserHosting.Http();
    while (true)
    {
        if (!driverProcess.Alive) throw new InvalidOperationException("Owned driver exited before readiness.");
        try { using var response = await http.GetAsync("http://127.0.0.1:" + port + "/status", token); if (response.IsSuccessStatusCode) break; }
        catch (HttpRequestException) { }
        await Task.Delay(20, token);
    }
    controller = new CdpController(endpoint, []); await controller.InitializeAsync(token);
    executor = new DetachAwareCommandExecutor(new LoopbackExecutor(new Uri("http://127.0.0.1:" + port), TimeSpan.FromSeconds(10)));
    driver = await Task.Run(() => new RemoteWebDriver(executor, Attachment.CreateOptions(endpoint).ToCapabilities())).WaitAsync(token);
    var ownedDriver = driverProcess;
    browser = new Browser(new() { Driver = new() { ExecutablePath = "unused" } }, new Attachment(driverProcess, port, executor, driver), _ => Task.CompletedTask, TimeSpan.FromSeconds(5));
    browser.BindElementTargetForTest(controller, controller.Controlled!.Value, driver.CurrentWindowHandle);
    driverProcess = null; executor = null; driver = null; controller = null; // Browser now owns these resources.
    await browser.NavigateAsync(new Uri(Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_URL")! + "/reference/interactions"), cancellationToken: token);
    var observed = await HighLevelInteractionReference.ObserveAsync(browser, Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_FACADE") == "legacy");
    report["observed"] = observed; HighLevelInteractionReference.Check(observed);
    await HighLevelInteractionReference.CheckBrowserVersionAsync(browser, report);
    report["chromePreserved"] = !chrome.HasExited && chrome.StartTime == creation; report["sessionChanged"] = false;
    await browser.StopAsync();
    if (ownedDriver.Alive) throw new InvalidOperationException("Owned driver survived browser disposal.");
    report["passed"] = true;
}
catch (Exception error) { failure = error; report["error"] = error.GetType().Name; Console.Error.WriteLine(error); }
finally
{
    var clean = true;
    async Task Step(Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception error) { clean = false; report["cleanupError"] = error.GetType().Name; Console.Error.WriteLine(error); }
    }
    if (browser is not null) await Step(() => browser.DisposeAsync().AsTask());
    if (driverProcess is not null) await Step(async () => { driverProcess.Terminate(); using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await driverProcess.WaitAsync(limit.Token); driverProcess.Dispose(); });
    await Step(() => { executor?.Detach(); driver?.Dispose(); executor?.Dispose(); return Task.CompletedTask; });
    if (controller is not null) await Step(() => controller.DisposeAsync().AsTask());
    await Step(async () => { if (!chrome.HasExited) chrome.Kill(entireProcessTree: true); await chrome.WaitForExitAsync(); await stdout; await stderr; });
    await Step(() => { Directory.Delete(profile, true); return Task.CompletedTask; });
    report["cleanupPassed"] = clean && chrome.HasExited;
    if (!clean) report["passed"] = false;
    await HighLevelInteractionReference.WriteAsync(report);
}
return failure is null && (bool)report["passed"]! && (bool)report["cleanupPassed"]! ? 0 : 1;
