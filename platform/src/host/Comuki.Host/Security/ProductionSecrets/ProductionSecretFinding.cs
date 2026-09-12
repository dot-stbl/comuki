namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// One outcome of the production-secret audit (issue #56): the checked
/// secret's name, the severity and a human-readable detail. Fail details
/// carry the exact startup-gate message — <c>ProductionSecretValidator</c>
/// throws them verbatim, <c>comuki doctor</c> prints them as fail lines.
/// </summary>
/// <param name="Name">Short check name (e.g. <c>apikey-pepper</c>).</param>
/// <param name="Severity">Ok when overridden, Warn when a dev default is tolerated outside Production, Fail when the startup gate would refuse.</param>
/// <param name="Detail">Human-readable outcome.</param>
public sealed record ProductionSecretFinding(string Name, ProductionSecretFinding.Severity SeverityLevel, string Detail)
{
    /// <summary>Outcome severity of one audited secret.</summary>
    public enum Severity
    {
        /// <summary>The secret is properly overridden.</summary>
        Ok,

        /// <summary>A dev default is present — tolerated outside Production.</summary>
        Warn,

        /// <summary>The startup gate refuses to boot with this value in Production.</summary>
        Fail,
    }
}
