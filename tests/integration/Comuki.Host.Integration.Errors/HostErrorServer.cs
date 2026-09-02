using Comuki.Host.Errors;
using Comuki.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Comuki.Host.Integration.Errors;

/// <summary>
/// Boots the central <see cref="ProviderExceptionHandler"/> against an
/// in-memory pipeline (TestServer, no Kestrel, no Postgres). The
/// handler is the host-global concern under test, so we exercise it
/// without the persistence stack or OIDC — same wiring
/// <c>HostComposer</c> registers (one <c>AddExceptionHandler</c>
/// line), one <c>UseExceptionHandler()</c> middleware, one
/// <c>AddProblemDetails()</c>.
/// </summary>
public sealed class HostErrorServer : IAsyncLifetime
{
    private WebApplication application = null!;

    /// <summary>In-memory <see cref="HttpClient"/> bound to the pipeline.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Comuki.Host.Errors",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // Wire the same three calls HostComposer makes for the central handler.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProviderExceptionHandler>();

        application = builder.Build();
        application.UseExceptionHandler();

        // Test-only throw endpoints — one per documented mapping row in
        // error-mapping.md §4, plus an unknown-class fallback and a
        // DomainException subclass to exercise the type-tree walk.
        application.MapGet("/test/throw/provider-base", static () =>
        {
            throw new ProviderException("provider.network_error", "the upstream service did not respond");
        });
        application.MapGet("/test/throw/provider-timeout", static () =>
        {
            throw new ProviderTimeoutException();
        });
        application.MapGet("/test/throw/provider-not-found", static () =>
        {
            throw new ProviderNotFoundException("provider.not_found", "upstream resource 'foo' is gone");
        });
        application.MapGet("/test/throw/domain", static () =>
        {
            throw new DomainException("domain.conflict", "the aggregate is in an illegal state");
        });
        application.MapGet("/test/throw/unknown", static () =>
        {
            throw new InvalidOperationException("kaboom");
        });
        application.MapGet("/test/throw/domain-subclass", static () =>
        {
            throw new HostErrorServerDomainSubclass("subclass.of.domain", "a domain subclass was thrown");
        });

        await application.StartAsync();
        Client = application.GetTestClient();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
    }

    /// <summary>Local <see cref="DomainException"/> subclass — proves the type-tree walk catches derived exceptions without listing them in the handler.</summary>
    private sealed class HostErrorServerDomainSubclass(string code, string message) : DomainException(code, message);
}
