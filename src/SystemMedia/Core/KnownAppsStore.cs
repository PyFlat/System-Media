using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SystemMedia.Core;

// "appUserModelId" is the 1.x field name, kept so old files still load.
internal sealed record RememberedApp([property: JsonPropertyName("appUserModelId")] string AppId, string? Name);

// The host only resolves listed instance ids, so apps are remembered to keep widgets bound while closed.
internal sealed class KnownAppsStore(string? dataDirectory, ILogger logger)
{
	private const string FileName = "known-apps.json";

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private readonly string? _path = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);

	internal IReadOnlyList<RememberedApp> Load()
	{
		if (_path is null || !File.Exists(_path))
		{
			return [];
		}

		try
		{
			return Parse(File.ReadAllText(_path));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			logger.Warning(exception, "Could not read the remembered media apps from {Path}; starting with none.", _path);
			return [];
		}
	}

	internal void Save(IReadOnlyList<RememberedApp> apps)
	{
		if (_path is null)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(_path, JsonSerializer.Serialize(apps, _json));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.Warning(exception, "Could not save the remembered media apps to {Path}.", _path);
		}
	}

	// 1.0.0 saved a plain list of ids.
	internal static IReadOnlyList<RememberedApp> Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var apps = new List<RememberedApp>();
		foreach (var element in document.RootElement.EnumerateArray())
		{
			if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } legacyId)
			{
				apps.Add(new RememberedApp(legacyId, Name: null));
			}
			else if (element.ValueKind == JsonValueKind.Object &&
				element.Deserialize<RememberedApp>(_json) is { AppId.Length: > 0 } app)
			{
				apps.Add(app);
			}
		}

		return apps;
	}
}
