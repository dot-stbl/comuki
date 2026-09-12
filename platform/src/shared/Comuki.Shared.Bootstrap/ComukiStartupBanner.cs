using Comuki.Shared.Bootstrap.Versioning;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap;

/// <summary>
/// Emits the build version line as the first log record of a starting host
/// (issue #56) — the same flat string the <c>version</c> subcommand prints,
/// wrapped in the comuki log format under the <c>comuki.host</c> category.
/// Uses a pre-formatted state so no structured fields duplicate the message.
/// </summary>
public static class ComukiStartupBanner
{
    /// <summary>Log category the banner is emitted under.</summary>
    public const string Category = "comuki.host";

    /// <summary>Writes the version banner through the supplied logger factory.</summary>
    /// <param name="loggerFactory">The host's logger factory.</param>
    /// <param name="product">Binary name (<c>comuki</c>, <c>comuki-brain</c>, …).</param>
    /// <param name="information">Build information of the entry assembly.</param>
    public static void Emit(ILoggerFactory loggerFactory, string product, ComukiBuildInformation information)
    {
        loggerFactory.CreateLogger(Category).Log(
            LogLevel.Information,
            default,
            information,
            null,
            (state, _) => state.ToVersionLine(product));
    }
}
