using Comuki.Host.Translator.Api.Models.Responses;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Composes <see cref="PiEnvironment.FromClaim"/> with
/// <see cref="PiCodingAgentDirectory"/> into a single per-execution
/// resource for <see cref="Loop.PiPump"/>. When the claim carries a
/// minted proxy (issue #122), this provisions a fresh
/// <c>PI_CODING_AGENT_DIR</c> so vendored pi 0.85.1 honors the proxy for
/// its cataloged Anthropic models (issue #150); when the claim carries
/// no proxy, it stays a pass-through of <see cref="PiEnvironment.FromClaim"/>'s
/// existing behavior. <see cref="DisposeAsync"/> deletes the temporary
/// directory on every exit path — the <see cref="Loop.PiPump"/>'s
/// <c>await using</c> at the top of its method body guarantees cleanup
/// on success, cancellation, and pi failure alike.
/// </summary>
public sealed class PiExecutionEnvironment : IAsyncDisposable
{
    private readonly string? directory;

    private PiExecutionEnvironment(IReadOnlyDictionary<string, string>? environment, string? directory)
    {
        Environment = environment;
        this.directory = directory;
    }

    /// <summary>
    /// Environment dict to pass to <see cref="Runtime.IPiRunner.RunAsync"/>; <c>null</c>
    /// when the claim carries no proxy, meaning pi runs with the inherited
    /// environment untouched.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; }

    /// <summary>
    /// Prepares the per-execution environment stamp. When the claim carries a proxy,
    /// reads the ambient <c>PI_CODING_AGENT_DIR</c> ONCE here (the only legitimate
    /// place to read process-wide env in this composition — <see cref="PiCodingAgentDirectory"/>
    /// stays a pure function of its arguments) and uses it as the source for the
    /// recursive copy. Otherwise returns a pass-through.
    /// </summary>
    /// <param name="claimed">The claim this cycle executes.</param>
    /// <param name="root">Parent directory for the fresh per-execution subfolder (typically <c>Path.GetTempPath()</c>).</param>
    /// <param name="cancellationToken">Propagated to <see cref="PiCodingAgentDirectory.ProvisionAsync"/>.</param>
    public static async Task<PiExecutionEnvironment> PrepareAsync(
        ClaimedWorkItemResponse claimed,
        string root,
        CancellationToken cancellationToken = default)
    {
        var baseEnvironment = PiEnvironment.FromClaim(claimed);
        if (baseEnvironment is null)
        {
            return new PiExecutionEnvironment(null, null);
        }

        var existingAgentDirectory = System.Environment.GetEnvironmentVariable(PiCodingAgentDirectory.EnvironmentVariable);
        var directory = await PiCodingAgentDirectory.ProvisionAsync(
            claimed.ProxyBaseUrl!,
            root,
            existingAgentDirectory,
            cancellationToken);

        return new PiExecutionEnvironment(
            new Dictionary<string, string>(baseEnvironment, StringComparer.Ordinal)
            {
                [PiCodingAgentDirectory.EnvironmentVariable] = directory,
            },
            directory);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        PiCodingAgentDirectory.Cleanup(directory);
        return ValueTask.CompletedTask;
    }
}
