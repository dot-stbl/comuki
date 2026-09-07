using Comuki.Modules.Intake.Domain.Connections;

namespace Comuki.Modules.Intake.Application.Views;

/// <summary>
/// Wire shape of a secret-rotation result (issue #46). Carries the
/// freshly-generated secret exactly once — the operator needs it to
/// configure the tracker; it does not appear in any other endpoint
/// response (list / get / source-connection-view).
/// </summary>
/// <param name="SourceId">Connection whose secret was rotated.</param>
/// <param name="Secret">Plaintext secret (hex). Disclosed only here.</param>
/// <param name="SecretEnvRef">Env-var name the connection still points to.</param>
/// <param name="RotatedAt">Wall-clock the rotation was stamped.</param>
public sealed record SecretRotationResponse(
    Guid SourceId,
    string Secret,
    string SecretEnvRef,
    DateTimeOffset RotatedAt)
{
    /// <summary>Maps the rotation outcome.</summary>
    /// <param name="connection"></param>
    /// <param name="newSecret"></param>
    /// <returns></returns>
    public static SecretRotationResponse Of(SourceConnection connection, string newSecret)
    {
        return new SecretRotationResponse(
            connection.Id.Value,
            newSecret,
            connection.SecretEnvRef,
            connection.UpdatedAt);
    }
}
