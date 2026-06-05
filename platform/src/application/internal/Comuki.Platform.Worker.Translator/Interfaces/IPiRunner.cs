namespace Comuki.Platform.Worker.Translator.Interfaces;

/// <summary>
/// Abstraction over <c>Process.Start(pi)</c> — the seam that lets unit tests
/// exercise the stream-json parser against recorded fixtures (this plan, 04-01)
/// instead of a real <c>pi</c> subprocess (lands in 04-03).
/// </summary>
public interface IPiRunner
{
}
