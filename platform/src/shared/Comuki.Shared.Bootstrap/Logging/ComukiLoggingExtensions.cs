using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Comuki.Shared.Bootstrap.Logging;

/// <summary>
/// Console logging with the comuki face (issue #54): registers the
/// console provider locked to the <c>comuki</c> formatter. Clear the
/// default providers first (<c>ClearProviders</c>) or every event is
/// rendered twice.
/// </summary>
public static class ComukiLoggingExtensions
{
    /// <summary>Adds the console provider locked to the comuki formatter.</summary>
    public static ILoggingBuilder AddComukiConsole(this ILoggingBuilder builder)
    {
        builder.AddConsole(static options => options.FormatterName = ComukiConsoleFormatter.FormatterName);
        builder.AddConsoleFormatter<ComukiConsoleFormatter, ConsoleFormatterOptions>();
        return builder;
    }
}
