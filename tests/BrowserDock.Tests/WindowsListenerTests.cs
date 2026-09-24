using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BrowserDock.Hosting;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class WindowsListenerTests
{
    [TestCase("127.0.0.1", true, true)]
    [TestCase("::", true, false)]
    [TestCase("0.0.0.0", false, false)]
    [TestCase("::1", false, false)]
    public async Task ListenerOwnershipRequiresExpectedProcessAndSupportedAddress(string address, bool driverAccepted, bool chromeAccepted)
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Requires Windows TCP ownership tables.");
        var listener = new TcpListener(IPAddress.Parse(address), 0);
        if (address == "::") listener.Server.DualMode = true;
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var process = Process.GetCurrentProcess();
            if (driverAccepted)
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(5));
                using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.That(BrowserHosting.OwnsDriverListener(process.Id, port), Is.EqualTo(driverAccepted));
            Assert.That(BrowserHosting.OwnsLoopbackListener(process.Id, port), Is.EqualTo(chromeAccepted));
            Assert.That(BrowserHosting.OwnsDriverListener(-1, port), Is.False, "Another process's listener must not be accepted.");
            Assert.That(BrowserHosting.OwnsDriverListener(process.Id, 0), Is.False, "The listener port must match.");
            listener.Stop();
            Assert.That(BrowserHosting.OwnsDriverListener(process.Id, port), Is.False, "Closed listeners must not be accepted.");
        }
        finally { listener.Stop(); }
    }
}
