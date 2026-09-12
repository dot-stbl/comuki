using Comuki.Shared.Bootstrap.Logging.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Comuki.Shared.Bootstrap.Logging;

/// <summary>
/// Console logging with the comuki face (issue #54): registers the
/// console provider locked to a <c>comuki</c> formatter — the flat line by
/// default, the JSON renderer under <c>COMUKI_LOG_FORMAT=json</c> (issue
/// #56 §4). Clear the default providers first (<c>ClearProviders</c>) or
/// every event is rendered twice. The configuration-aware overload also
/// applies the level override chain: <c>COMUKI_LOG_LEVEL</c> env first,
/// then <c>[logging] level</c> from config.toml.
/// </summary>
public static class ComukiLoggingExtensions
{
    /// <summary>Adds the console provider locked to the comuki formatter selected by <c>COMUKI_LOG_FORMAT</c>.</summary>
    public static ILoggingBuilder AddComukiConsole(this ILoggingBuilder builder)
    {
        return builder.AddComukiConsole(configuration: null);
    }

    /// <summary>Adds the comuki console provider and applies the <c>COMUKI_LOG_LEVEL</c> / <c>[logging] level</c> override chain.</summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configuration">The comuki configuration root ([logging] level); null skips the config layer.</param>
    public static ILoggingBuilder AddComukiConsole(this ILoggingBuilder builder, IConfiguration? configuration)
    {
        builder.AddConsole(static options => options.FormatterName = ComukiLogEnvironment.ResolveFormat() == ComukiLogEnvironment.Format.Json
            ? ComukiJsonConsoleFormatter.FormatterName
            : ComukiConsoleFormatter.FormatterName);
        builder.AddConsoleFormatter<ComukiConsoleFormatter, ConsoleFormatterOptions>();
        builder.AddConsoleFormatter<ComukiJsonConsoleFormatter, ConsoleFormatterOptions>();

        if (ComukiLogEnvironment.ResolveLevel(configuration) is { } minimumLevel)
        {
            builder.SetMinimumLevel(minimumLevel);
        }

        return builder;
    }
}
