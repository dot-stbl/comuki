using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Comuki.Host.Integration.Proxy;

/// <summary>
/// In-process HTTP listener that imitates an OpenAI-compatible
/// <c>/v1/chat/completions</c> endpoint for proxy integration tests. The
/// fake records every request it sees (path, body, headers) so tests
/// can assert the YARP rewrite worked; the response body is a static
/// OpenAI-shape JSON document so the proxy's usage extractor returns a
/// populated <c>ProxyUsageReport</c>.
/// </summary>
internal sealed class FakeUpstreamServer : IDisposable
{
    private readonly HttpListener listener;
    private readonly List<FakeUpstreamRequest> seen = [];

    private static readonly string chatCompletionResponseBody = /*lang=json,strict*/ """
    {
      "id": "chatcmpl-fake",
      "object": "chat.completion",
      "created": 1,
      "model": "gpt-4o-mini",
      "choices": [
        {
          "index": 0,
          "message": { "role": "assistant", "content": "hi" },
          "finish_reason": "stop"
        }
      ],
      "usage": { "prompt_tokens": 7, "completion_tokens": 11, "total_tokens": 18 }
    }
    """;

    /// <summary>Constructs the listener on a free loopback port.</summary>
    public FakeUpstreamServer()
    {
        var port = FreeTcpPort();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
    }

    /// <summary>Upstream base URL the proxy / virtual-key configuration points at.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Requests the fake has observed since the last reset.</summary>
    public IReadOnlyList<FakeUpstreamRequest> Seen => seen;

    /// <summary>Starts the listener on a background task.</summary>
    public Task StartAsync()
    {
        BaseAddress = new Uri(listener.Prefixes.First());
        listener.Start();
        _ = Task.Run(AcceptLoopAsync);
        return Task.CompletedTask;
    }

    /// <summary>Clears the recorded request log between tests.</summary>
    public void Reset()
    {
        lock (seen)
        {
            seen.Clear();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(async () => await HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        string? body = null;
        if (ctx.Request.HasEntityBody)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }

        var authorization = ctx.Request.Headers["Authorization"];
        var path = ctx.Request.Url?.AbsolutePath ?? string.Empty;

        lock (seen)
        {
            seen.Add(new FakeUpstreamRequest(path, authorization, body));
        }

        var responseBytes = Encoding.UTF8.GetBytes(chatCompletionResponseBody);
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = responseBytes.Length;
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        await ctx.Response.OutputStream.WriteAsync(responseBytes);
        ctx.Response.OutputStream.Close();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (listener.IsListening)
        {
            listener.Stop();
        }

        listener.Close();
    }

    /// <summary>Async disposal — stops the listener and releases the socket.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>One request the fake upstream observed.</summary>
/// <param name="Path">Absolute path the proxy forwarded.</param>
/// <param name="Authorization">The <c>Authorization</c> header value the proxy sent (or null).</param>
/// <param name="Body">Raw request body.</param>
public sealed record FakeUpstreamRequest(string Path, string? Authorization, string? Body);
