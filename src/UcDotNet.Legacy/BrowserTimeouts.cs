using Core = global::UcDotNet;

namespace UcDotNet.Legacy;

public sealed class BrowserTimeouts
{
    public TimeSpan ChromeStart { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan DevToolsEndpointDiscovery { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CdpConnect { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan DriverProcessStart { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan WebDriverSessionCreate { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan Navigation { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan Command { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DisconnectDrain { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan Reconnect { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan GracefulShutdown { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ForceKillWait { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProfileCleanup { get; set; } = TimeSpan.FromSeconds(5);
    internal Core.BrowserTimeouts Snapshot() => new()
    {
        ChromeStart = ChromeStart,
        DevToolsEndpointDiscovery = DevToolsEndpointDiscovery,
        CdpConnect = CdpConnect,
        DriverProcessStart = DriverProcessStart,
        WebDriverSessionCreate = WebDriverSessionCreate,
        Navigation = Navigation,
        Command = Command,
        DisconnectDrain = DisconnectDrain,
        Reconnect = Reconnect,
        GracefulShutdown = GracefulShutdown,
        ForceKillWait = ForceKillWait,
        ProfileCleanup = ProfileCleanup
    };
}
