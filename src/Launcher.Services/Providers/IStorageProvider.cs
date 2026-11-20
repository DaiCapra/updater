using System.Text.Json;
using Launcher.Core;
using Launcher.Core.Storage;

namespace Launcher.Services.Providers;

/// <summary>Turns the <c>updateSource</c> object of a profile into typed settings for one provider type.</summary>
public interface ISourceSettingsParser
{
    /// <summary>The <c>type</c> value in profiles, such as <c>google-drive</c>.</summary>
    string Type { get; }

    /// <summary>Parses the provider's fields. Relative paths resolve against <paramref name="baseDirectory"/>.</summary>
    ISourceSettings Parse(JsonElement json, string baseDirectory);
}

/// <summary>A file host (Google Drive, S3, ...) that releases can be listed, downloaded from and uploaded to.</summary>
public interface IStorageProvider : ISourceSettingsParser
{
    IReleaseStoreReader CreateReader(ISourceSettings settings);

    IReleaseStoreWriter CreateWriter(ISourceSettings settings);
}

/// <summary>An update source that isn't a file store, such as a JSON manifest that links to a download page.</summary>
public interface IUpdateSourceFactory : ISourceSettingsParser
{
    IUpdateSource Create(ISourceSettings settings);
}
