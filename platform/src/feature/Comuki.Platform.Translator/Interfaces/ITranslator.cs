namespace Comuki.Platform.Translator.Interfaces;

/// <summary>
/// Top-level contract for the Translator: turns a brief + a snapshot of code
/// into a stream of <c>PiEvent</c>s, with a final <c>StageReport</c>.
/// Real implementation (Process.Start(pi) + gRPC to Orchestrator) lands in 04-03;
/// this is a marker for 04-01's project skeleton.
/// </summary>
public interface ITranslator
{
}
