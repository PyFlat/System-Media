using System.Text.Json;
using Serilog;

namespace SystemMedia.Core;

internal sealed record PluginSettings
{
	public bool ExactYouTubeInfo { get; init; }
}

// Where 1.0.x kept its settings, switched by actions. Now only read, as the default until the settings page is saved.
internal sealed class PluginSettingsStore(string? dataDirectory, ILogger logger)
{
	private const string FileName = "settings.json";

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private readonly string? _path = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);

	internal PluginSettings Load()
	{
		if (_path is null || !File.Exists(_path))
		{
			return new PluginSettings();
		}

		try
		{
			return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(_path), _json) ?? new PluginSettings();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			logger.Warning(exception, "Could not read the plugin settings from {Path}; using the defaults.", _path);
			return new PluginSettings();
		}
	}
}
