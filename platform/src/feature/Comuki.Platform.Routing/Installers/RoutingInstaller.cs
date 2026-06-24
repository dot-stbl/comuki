using System.Net;
using Comuki.Platform.Routing.Forwarding;
using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Comuki.Platform.Routing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Comuki.Platform.Routing.Installers;

/// <summary>
/// DI-регистрация модуля Routing: ротация ключей Z.AI поверх YARP IHttpForwarder.
/// </summary>
public static class RoutingInstaller
{
    public static IServiceCollection AddRoutingCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RotationOptions>()
            .Bind(configuration.GetSection(RotationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.ExhaustionRules.All(
                    rule => rule.StatusCode is not null || !string.IsNullOrWhiteSpace(rule.BodyContains)),
                "Each exhaustion rule must set StatusCode or BodyContains.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IKeyPool, KeyPool>();
        services.AddSingleton<IQuotaExhaustionDetector, QuotaExhaustionDetector>();
        services.AddSingleton<IUpstreamSender, YarpUpstreamSender>();
        services.AddSingleton<IKeyRotatingForwarder, KeyRotatingForwarder>();

        // YARP direct forwarding: IHttpForwarder + выделенный HttpMessageInvoker.
        services.AddHttpForwarder();
        services.AddSingleton(CreateForwarderHttpClient());

        return services;
    }

    // HttpMessageInvoker для IHttpForwarder — рекомендованная YARP конфигурация
    // (без авто-редиректов/декомпрессии/куки; прокси пробрасывает поток как есть).
    private static HttpMessageInvoker CreateForwarderHttpClient()
        => new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
        });
}
