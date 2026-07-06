using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Comuki.Platform.Proxy.Integration.Rotation;

/// <summary>
/// Минимальный фейковый Z.AI: на ключ "dead" отвечает 429 (quota),
/// на ключ "live" — 200 с телом. Слушает на динамическом порту.
/// </summary>
public sealed class FakeUpstream : IAsyncDisposable
{
    private readonly WebApplication webApp;

    private FakeUpstream(WebApplication webApp) => this.webApp = webApp;

    public string Url { get; private set; } = string.Empty;

    public static async Task<FakeUpstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // динамический порт
        var builtApp = builder.Build();

        builtApp.Map("/{**catchAll}", async (HttpContext context) =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            if (auth.Contains("dead", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync(
                    """{"error":"rate_limit_exceeded"}""",
                    context.RequestAborted);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"type":"message","content":"ok-from-live-key"}""",
                context.RequestAborted);
        });

        await builtApp.StartAsync(CancellationToken.None);

        return new FakeUpstream(builtApp)
        {
            Url = builtApp.Urls.First(),
        };
    }

    public async ValueTask DisposeAsync() => await webApp.DisposeAsync();
}
