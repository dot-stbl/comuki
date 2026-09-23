using Xunit;

namespace Comuki.Modules.Projects.Integration.RedisCache;

/// <summary>
/// Collection definition binding <see cref="RedisFixture"/> to every test
/// class tagged <c>[Collection(nameof(RedisCollection))]</c>; parallelised
/// runs would race on the one shared container.
/// </summary>
[CollectionDefinition(nameof(RedisCollection), DisableParallelization = true)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
}
