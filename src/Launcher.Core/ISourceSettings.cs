namespace Launcher.Core;

/// <summary>Marker for the typed settings each update-source provider parses from a profile.</summary>
public interface ISourceSettings;

/// <summary>The update source selected by a profile: the provider type and its parsed settings.</summary>
public sealed record UpdateSourceConfig(string Type, ISourceSettings Settings);
