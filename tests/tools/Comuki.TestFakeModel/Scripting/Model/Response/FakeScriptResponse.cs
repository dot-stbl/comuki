namespace Comuki.TestFakeModel.Scripting.Model.Response;

/// <summary>The scripted reply: content blocks plus the stop reason and token usage to report.</summary>
public sealed record FakeScriptResponse(string StopReason, IReadOnlyList<FakeContentBlock> Content, FakeUsage? Usage);
