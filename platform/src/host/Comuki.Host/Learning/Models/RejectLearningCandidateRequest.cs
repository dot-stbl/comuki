namespace Comuki.Host.Learning.Models;

/// <summary>Reject body of the learning-candidates surface — carries the optional human reason.</summary>
/// <param name="Reason">Why the suggestion was rejected; null means "no reason given".</param>
public sealed record RejectLearningCandidateRequest(string? Reason);
