namespace Comuki.Host.Translator.Execution.Workspace;

/// <summary>
/// Typed failure of the workspace prepare step (issue #125): the item must
/// be failed and pi must not start. The <see cref="Code"/> is a stable
/// dot.case machine id — it rides into the fail reason; <see cref="Exception.Message"/>
/// never contains the git credential.
/// </summary>
public sealed class WorkspacePrepareException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The claim carried no source git URL — the project has no product repository configured.</summary>
    public const string SourceMissingCode = "workspace.source_missing";

    /// <summary>The source git URL is not HTTPS — the fence allows no other scheme.</summary>
    public const string SourceNotHttpsCode = "workspace.source_not_https";

    /// <summary>The configured credential contains characters a git config file cannot carry safely.</summary>
    public const string CredentialInvalidCode = "workspace.credential_invalid";

    /// <summary>The git clone itself failed (bad ref, auth, network).</summary>
    public const string CloneFailedCode = "workspace.clone_failed";

    /// <summary>Stable dot.case id of the failure class.</summary>
    public string Code { get; } = code;
}
