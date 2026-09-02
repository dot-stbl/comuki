namespace Comuki.Modules.Intake.Domain.Rules;

/// <summary>
/// The two admission modes (scope-draft §1): <see cref="Watch"/> admits
/// matching tickets straight into a run; <see cref="Inbox"/> parks them
/// in the inbox until a human claims them.
/// </summary>
public enum AdmissionMode
{
    /// <summary>Webhook match → run immediately (AppSet-style watch + filter).</summary>
    Watch,

    /// <summary>Webhook/catalog match → pending ticket; a human claims it into a run.</summary>
    Inbox,
}
