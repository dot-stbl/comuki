using Comuki.Modules.Verify.Application.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Verify.Application;

/// <summary>Registration entry point for the Verify module application layer.</summary>
public static class VerifyApplicationExtensions
{
    /// <summary>
    /// Registers the verify module application layer. The persistence
    /// port and the runner are wired by the Infrastructure extension;
    /// the worker hosted-service is also registered there (it owns the
    /// EF context and the comuki worker registry registration). Binding
    /// + validation of <see cref="VerifyOptions"/> happens in
    /// <c>HostComposer</c> like every other module — this call only
    /// registers the (unbound) options placeholder so consumers can
    /// resolve <c>IOptions&lt;VerifyOptions&gt;</c> in tests without a
    /// host.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddVerifyApplication(this IServiceCollection services)
    {
        services.AddOptions<VerifyOptions>();
        return services;
    }
}
