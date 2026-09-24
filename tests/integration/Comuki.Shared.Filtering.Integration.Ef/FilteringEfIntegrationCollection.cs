using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Shared.Filtering.Integration.Ef;

/// <summary>
/// One shared Postgres for the whole filter-Ef suite (WS2 follow-up), never
/// in parallel: the same one-container-per-collection contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
/// <remarks>
/// This suite is small (one test class, ten facts), but every test seeds five
/// fresh <c>Run</c> rows and asserts on exact counts/order — a shared
/// container without this collection would carry rows across tests and every
/// assertion would break. <see cref="PostgresCollectionFixture.ResetDatabaseAsync"/>
/// at the top of <see cref="RunsFilteringShould.InitializeAsync"/> truncates
/// the orchestration schema back to empty before each seed.
/// </remarks>
[CollectionDefinition(nameof(FilteringEfIntegrationCollection), DisableParallelization = true)]
public sealed class FilteringEfIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
