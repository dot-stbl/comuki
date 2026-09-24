using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// One shared Postgres for the whole PiCli suite (WS2 follow-up), never in
/// parallel: the same one-container-per-collection contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
/// <remarks>
/// Only <see cref="TranslatorE2EShould"/> touches Postgres — the other two
/// test classes (<c>PiRunnerShould</c>, <c>WorkerGrpcServerShould</c>) and the
/// host helper <c>TestWorkerHost</c> are intentionally NOT in this collection:
/// they don't carry a database and need no reset. They run in parallel with
/// this collection's tests when xUnit's default collection-parallelism
/// schedules them, which is fine — they don't share state with
/// <see cref="TranslatorE2EShould"/>.
/// </remarks>
[CollectionDefinition(nameof(PiCliIntegrationCollection), DisableParallelization = true)]
public sealed class PiCliIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
