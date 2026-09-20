using Comuki.Engine.Compute.Installers;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using k8s;
using k8s.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Provider-selection wiring of <see cref="ComputeInstaller.AddComukiCompute"/>:
/// Compute:Provider picks the concrete behind <see cref="IComputeProvider"/>
/// (factory, both registered; the unselected one never constructed).
/// </summary>
public sealed class ComputeInstallerShould
{
    [Theory(DisplayName = "Given no kubeconfig path, when building Kubernetes configuration, then uses in-cluster configuration")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UseInClusterConfigurationWithoutLocalhostFallback(string? kubeconfigPath)
    {
        var inClusterConfiguration = new KubernetesClientConfiguration
        {
            Host = "https://10.96.0.1",
        };

        var configuration = KubernetesClientConfigurationFactory.Build(
            kubeconfigPath,
            () => inClusterConfiguration,
            _ => throw new InvalidOperationException("External kubeconfig must not be read"));

        configuration.ShouldBeSameAs(inClusterConfiguration);
        configuration.Host.ShouldNotBe("http://localhost:8080");
    }

    [Fact(DisplayName = "Given explicit kubeconfig path, when building Kubernetes configuration, then reads that file")]
    public void UseExternalKubeconfigWhenPathIsExplicit()
    {
        const string KubeconfigPath = "clusters/worker.kubeconfig";
        string? observedPath = null;
        var externalConfiguration = new KubernetesClientConfiguration
        {
            Host = "https://worker.example.test",
        };

        var configuration = KubernetesClientConfigurationFactory.Build(
            KubeconfigPath,
            () => throw new InvalidOperationException("In-cluster configuration must not be read"),
            path =>
            {
                observedPath = path;
                return externalConfiguration;
            });

        configuration.ShouldBeSameAs(externalConfiguration);
        observedPath.ShouldBe(KubeconfigPath);
    }

    [Fact(DisplayName = "When resolve Docker Provider By Default, then test passes")]
    public void ResolveDockerProviderByDefault()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<DockerComputeProvider>();
    }

    [Fact(DisplayName = "When resolve Kubernetes Provider When Configured, then test passes")]
    public void ResolveKubernetesProviderWhenConfigured()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Compute:Provider"] = "kubernetes",
        });

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<KubernetesComputeProvider>();
    }

    [Fact(DisplayName = "Given docker provider, when resolving IComputeProvider, then Kubernetes client factory is not invoked")]
    public void DoNotInvokeKubernetesClientFactoryUnderDockerProvider()
    {
        var factory = Substitute.For<IKubernetesClientConfigurationFactory>();
        using var provider = BuildProvider(
            settings: [],
            kubernetesClientConfigurationFactory: factory,
            captureLoggerFactory: out _);

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<DockerComputeProvider>();

        factory.DidNotReceive().Build(Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given kubernetes provider with no kubeconfig, when the in-cluster factory throws, then the host rethrows and logs an actionable error")]
    public void RethrowAndLogErrorWhenInClusterFactoryThrows()
    {
        var factory = Substitute.For<IKubernetesClientConfigurationFactory>();
        var exception = new KubernetesClientException(
            "Unable to load in-cluster configuration, service account token is missing");
        factory.Build(Arg.Any<string?>()).Throws(exception);

        var capture = new CapturingLoggerProvider();
        using var provider = BuildProvider(
            settings: new Dictionary<string, string?> { ["Compute:Provider"] = "kubernetes" },
            kubernetesClientConfigurationFactory: factory,
            captureLoggerFactory: out var loggerFactory);
        loggerFactory.AddProvider(capture);

        var thrown = Should.Throw<KubernetesClientException>(provider.GetRequiredService<IKubernetes>);
        thrown.ShouldBeSameAs(exception);

        var errorEntry = capture.Entries.SingleOrDefault(static entry =>
            entry.Level == LogLevel.Error
            && entry.Category == "Comuki.Compute.KubernetesClient");
        errorEntry.ShouldNotBeNull();
        errorEntry.Exception.ShouldBeSameAs(exception);
        errorEntry.Message.ShouldContain("Kubernetes client failed to initialise");
        errorEntry.Message.ShouldContain("ServiceAccount");
        errorEntry.Message.ShouldContain("Compute:Provider=docker");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        return BuildProvider(settings, null, out _);
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        IKubernetesClientConfigurationFactory? kubernetesClientConfigurationFactory,
        out ILoggerFactory captureLoggerFactory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddComukiCompute(configuration);

        // Substitute the container-operations facade so the docker socket
        // is not touched when the docker concretes resolve. The IKubernetes
        // client is only substituted when the test does NOT install a custom
        // KubernetesClientConfigurationFactory — otherwise the test needs
        // the real IKubernetes factory to run (and throw) so the host
        // rethrow path is exercised.
        services.AddSingleton(Substitute.For<IContainerOperations>());
        if (kubernetesClientConfigurationFactory is null)
        {
            services.AddSingleton(Substitute.For<IKubernetes>());
        }

        if (kubernetesClientConfigurationFactory is not null)
        {
            services.RemoveAll<IKubernetesClientConfigurationFactory>();
            services.AddSingleton(kubernetesClientConfigurationFactory);
        }

        var sp = services.BuildServiceProvider();
        captureLoggerFactory = sp.GetRequiredService<ILoggerFactory>();
        return sp;
    }

    /// <summary>Minimal in-memory logger provider used to assert LogError was emitted with the expected shape.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, Entries);
        }

        public void Dispose() { }

        private sealed class CapturingLogger(string category, List<LogEntry> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                sink.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
            }
        }
    }

    /// <summary>Single captured log record exposed by <see cref="CapturingLoggerProvider"/>.</summary>
    public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);
}
