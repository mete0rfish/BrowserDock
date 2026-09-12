using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ReferenceFixtureTests
{
    [Test]
    public async Task SharedFixtureServesRealRedirectAndNestedFrameDocuments()
    {
        await using var server = await WindowsTests.FixtureAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = server.Url };
        using var redirect = await client.GetAsync("/reference/redirect");
        Assert.That((int)redirect.StatusCode, Is.EqualTo(302));
        Assert.That(redirect.Headers.Location!.OriginalString, Is.EqualTo("/reference/page"));
        var page = await client.GetStringAsync("/reference/page");
        Assert.That(page, Does.Contain("<title>BrowserDock reference</title>"));
        Assert.That(await client.GetStringAsync("/reference/nested"), Does.Contain("/reference/frame"));
        Assert.That(await client.GetStringAsync("/reference/frame"), Does.Contain("frame-input"));
        using var missing = await client.GetAsync("/reference/missing");
        Assert.That((int)missing.StatusCode, Is.EqualTo(404));
    }
}
