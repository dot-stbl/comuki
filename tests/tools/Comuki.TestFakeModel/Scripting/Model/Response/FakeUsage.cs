namespace Comuki.TestFakeModel.Scripting.Model.Response;

/// <summary>
/// Token usage a scripted response reports. Unset fields fall back to
/// <c>Comuki.TestFakeModel.Determinism.UsageDefaults</c> at response-generation time.
/// </summary>
public sealed record FakeUsage(int? InputTokens = null, int? OutputTokens = null);
