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
            "/reference/interactions" => """
                <!doctype html><meta charset="utf-8"><title>High-level reference</title>
                <div id=status style="display:none">pending</div>
                <button id=button disabled style="display:none" onclick="window.clicks++">click</button>
                <input id=input value=initial><div id=marker>visible</div>
                <iframe id=frame src=/reference/frame></iframe>
                <script>
                window.clicks=0;
                window.armStatus=()=>setTimeout(()=>{statusElement.style.display='block';statusElement.textContent='almost ready now'},150);
                const statusElement=document.getElementById('status');
                window.armExact=()=>setTimeout(()=>statusElement.textContent='ready',150);
                window.armButton=()=>setTimeout(()=>{const b=document.getElementById('button');b.disabled=false;b.style.display='block'},150);
                window.armHide=()=>setTimeout(()=>document.getElementById('marker').style.display='none',150);
                window.armRemove=()=>setTimeout(()=>document.getElementById('marker').remove(),150);
                </script>
                """,
            "/reference/nested" => "<!doctype html><iframe id=inner src=/reference/frame></iframe>",
            _ => null
        };
        if (html is null) context.Response.StatusCode = 404;
        else await context.Response.WriteAsync(html, context.RequestAborted);
        return true;
    }
}
