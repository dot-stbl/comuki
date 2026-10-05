namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// The kind of a Wiki page. Wire key: <see cref="WikiPageKindKeys"/>.
/// </summary>
public enum WikiPageKind
{
    /// <summary>Definition / glossary entry.</summary>
    Glossary = 1,

    /// <summary>Step-by-step procedure or runbook.</summary>
    HowTo = 2,

    /// <summary>Decision record (why a choice was made, what was rejected).</summary>
    DecisionRecord = 3,

    /// <summary>Incident write-up (what failed, recovery, prevention).</summary>
    Incident = 4,

    /// <summary>External reference or canonical pointer to another source.</summary>
    Reference = 5,
}
