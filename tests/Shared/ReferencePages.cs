using Microsoft.AspNetCore.Http;

namespace BrowserDock.TestFixtures;

// Linked into both servers so Python and .NET receive byte-identical pages.
internal static class ReferencePages
{
    public static async Task<bool> HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is null || !path.StartsWith("/reference/", StringComparison.Ordinal)) return false;
        context.Response.Headers.CacheControl = "no-store";
        if (path == "/reference/redirect") { context.Response.Redirect("/reference/page"); return true; }
        context.Response.ContentType = "text/html; charset=utf-8";
        var html = path switch
        {
            "/reference/page" => """
                <!doctype html><meta charset="utf-8"><title>BrowserDock reference</title>
                <button id="button" onclick="this.textContent='clicked'">ready</button>
                <input id="input"><iframe id="frame" src="/reference/frame"></iframe>
                <iframe id="outer" src="/reference/nested"></iframe>
                <script>setTimeout(() => { const e=document.createElement('span');e.id='delayed';e.textContent='available';document.body.append(e); }, 150);</script>
                """,
            "/reference/frame" => "<!doctype html><input id=frame-input>",
            "/reference/nested" => "<!doctype html><iframe id=inner src=/reference/frame></iframe>",
            _ => null
        };
        if (html is null) context.Response.StatusCode = 404;
        else await context.Response.WriteAsync(html, context.RequestAborted);
        return true;
    }
}
