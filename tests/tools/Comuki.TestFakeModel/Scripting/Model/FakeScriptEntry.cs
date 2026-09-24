using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.Scripting.Model;

/// <summary>
/// One scripted request/response pairing. <see cref="RequestIndex"/> pins
/// this entry to an exact 1-based request number; leave it <c>null</c> for
/// the common sequential case.
/// </summary>
public sealed record FakeScriptEntry(int? RequestIndex, FakeScriptMatch? Match, FakeScriptResponse Response);
