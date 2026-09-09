using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for the Knowledge configuration classes —
/// <see cref="KnowledgeIngestOptions"/> and
/// <see cref="KnowledgeEmbeddingOptions"/> — defaults, section name, and
/// the env-var resolver for the OpenAI key.
/// </summary>
public sealed class KnowledgeOptionsShould
{
    [Fact(DisplayName = "Given KnowledgeIngestOptions defaults, when read, then ChunkTokenTarget is 500 and PollIntervalSeconds is 60")]
    public void IngestOptionsDefaultsAreSensible()
    {
        var options = new KnowledgeIngestOptions();

        KnowledgeIngestOptions.SectionName.ShouldBe("Knowledge:Ingest");
        options.ChunkTokenTarget.ShouldBe(500);
        options.PollIntervalSeconds.ShouldBe(60);
    }

    [Fact(DisplayName = "Given KnowledgeEmbeddingOptions defaults, when read, then provider is noop, model is text-embedding-3-small, dimensions is 1536, batch is 32")]
    public void EmbeddingOptionsDefaultsAreSensible()
    {
        var options = new KnowledgeEmbeddingOptions();

        KnowledgeEmbeddingOptions.SectionName.ShouldBe("Knowledge:Embedding");
        options.Provider.ShouldBe(EmbeddingProviderKindKeys.Noop);
        options.Model.ShouldBe("text-embedding-3-small");
        options.Dimensions.ShouldBe(1536);
        options.BatchSize.ShouldBe(32);
        options.ApiKeyEnvRef.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a wire-key Provider, when Kind is read, then it maps to the EmbeddingProviderKind enum")]
    public void EmbeddingOptionsKindMapsWireKeyToEnum()
    {
        var openai = new KnowledgeEmbeddingOptions { Provider = "openai" };
        var voyage = new KnowledgeEmbeddingOptions { Provider = "voyage" };
        var noop = new KnowledgeEmbeddingOptions { Provider = "noop" };

        openai.Kind.ShouldBe(EmbeddingProviderKind.OpenAi);
        voyage.Kind.ShouldBe(EmbeddingProviderKind.Voyage);
        noop.Kind.ShouldBe(EmbeddingProviderKind.Noop);
    }

    [Fact(DisplayName = "Given ApiKeyEnvRef is unset, when ResolveApiKey is called, then it returns null")]
    public void EmbeddingOptionsResolveApiKeyUnset()
    {
        var options = new KnowledgeEmbeddingOptions();

        options.ResolveApiKey().ShouldBeNull();
    }

    [Fact(DisplayName = "Given ApiKeyEnvRef is whitespace, when ResolveApiKey is called, then it returns null")]
    public void EmbeddingOptionsResolveApiKeyWhitespace()
    {
        var options = new KnowledgeEmbeddingOptions { ApiKeyEnvRef = "   " };

        options.ResolveApiKey().ShouldBeNull();
    }

    [Fact(DisplayName = "Given ApiKeyEnvRef points at an env var, when ResolveApiKey is called, then it returns the env var value")]
    public void EmbeddingOptionsResolveApiKeyReadsEnvironment()
    {
        var envName = "COMUKI_TEST_KNOWLEDGE_API_KEY";
        Environment.SetEnvironmentVariable(envName, "sk-secret-value");
        try
        {
            var options = new KnowledgeEmbeddingOptions { ApiKeyEnvRef = envName };

            options.ResolveApiKey().ShouldBe("sk-secret-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact(DisplayName = "Given ApiKeyEnvRef points at an unset env var, when ResolveApiKey is called, then it returns null")]
    public void EmbeddingOptionsResolveApiKeyMissingEnvironmentVariable()
    {
        var envName = "COMUKI_TEST_KNOWLEDGE_API_KEY_DEFINITELY_UNSET_xyz";
        Environment.SetEnvironmentVariable(envName, null);

        var options = new KnowledgeEmbeddingOptions { ApiKeyEnvRef = envName };

        options.ResolveApiKey().ShouldBeNull();
    }
}
