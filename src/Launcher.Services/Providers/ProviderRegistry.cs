using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Launcher.Core;

namespace Launcher.Services.Providers;

/// <summary>Looks up the registered providers by their profile <c>type</c>.</summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ISourceSettingsParser> providers = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRegistry(IEnumerable<IStorageProvider> storageProviders, IEnumerable<IUpdateSourceFactory> sourceFactories)
    {
        foreach (var provider in storageProviders.Cast<ISourceSettingsParser>().Concat(sourceFactories))
        {
            if (!providers.TryAdd(provider.Type, provider))
            {
                throw new ArgumentException($"More than one provider is registered for the update source type '{provider.Type}'.");
            }
        }
    }

    public IEnumerable<string> Types => providers.Keys.Order(StringComparer.OrdinalIgnoreCase);

    public UpdateSourceConfig Parse(string type, JsonElement json, string baseDirectory) =>
        new(type, Get(type).Parse(json, baseDirectory));

    public bool TryGetStorage(string type, [NotNullWhen(true)] out IStorageProvider? provider)
    {
        provider = providers.GetValueOrDefault(type) as IStorageProvider;
        return provider is not null;
    }

    public IStorageProvider GetStorage(string type) =>
        Get(type) as IStorageProvider
        ?? throw new InvalidDataException($"The update source type '{type}' is not a file store, so it can't be published to.");

    public IUpdateSourceFactory GetSourceFactory(string type) =>
        Get(type) as IUpdateSourceFactory
        ?? throw new InvalidDataException($"The update source type '{type}' is a file store, not a standalone update source.");

    private ISourceSettingsParser Get(string type) =>
        providers.GetValueOrDefault(type)
        ?? throw new InvalidDataException(
            $"The update source type '{type}' is unknown. Use one of: {string.Join(", ", Types)}.");
}
