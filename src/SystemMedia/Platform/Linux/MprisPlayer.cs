using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform.Linux;

internal sealed record MprisPlayer(string BusName, string? Identity, string? DesktopEntry, IReadOnlyDictionary<string, object?> Properties)
{
	private const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

	internal string? Variant { get; private init; }

	// "@" never appears in a bus name, so a variant cannot collide with another player.
	internal string AppId => Variant is null ? AppIdOf(BusName) : $"{AppIdOf(BusName)}@{Variant}";

	internal bool IsInstance => BusName.Contains(".instance", StringComparison.Ordinal);

	internal bool IsPlaying => string.Equals(Text("PlaybackStatus"), "Playing", StringComparison.Ordinal);

	internal string? ArtUrl => MetadataText("mpris:artUrl");

	internal string? TrackId => MetadataText("mpris:trackid") is { } id && !string.Equals(id, NoTrack, StringComparison.Ordinal) ? id : null;

	internal long? PositionMicros => Number(Properties, "Position") is { } position ? (long)position : null;

	internal double? Volume => Number(Properties, "Volume");

	internal bool CanControl => Flag("CanControl") ?? true;

	internal bool Can(string capability) => CanControl && (Flag(capability) ?? true);

	internal bool Has(string property) => Properties.ContainsKey(property);

	// Players that run several times append ".instance<id>" (Firefox, Chromium); every instance is one app.
	internal static string AppIdOf(string busName)
	{
		var name = busName.StartsWith(MprisBus.NamePrefix, StringComparison.Ordinal) ? busName[MprisBus.NamePrefix.Length..] : busName;
		var instance = name.IndexOf(".instance", StringComparison.Ordinal);
		return instance > 0 ? name[..instance] : name;
	}

	// A web app's own .desktop file is named after its window class and gives it its name and icon.
	internal MprisPlayer LaunchedAs(string variant, string? desktopName) => this with
	{
		Variant = variant,
		Identity = desktopName ?? $"{Identity ?? AppIdOf(BusName)} ({variant})",
		DesktopEntry = desktopName is null ? DesktopEntry : variant,
	};

	internal MediaSnapshot ToSnapshot(DateTimeOffset readAt)
	{
		var length = Number(Metadata, "mpris:length");
		var loop = Text("LoopStatus");
		return new MediaSnapshot
		{
			AppId = AppId,
			AppName = Identity ?? AppId,
			Title = MetadataText("xesam:title"),
			Artist = MetadataNames("xesam:artist"),
			AlbumArtist = MetadataNames("xesam:albumArtist"),
			AlbumTitle = MetadataText("xesam:album"),
			HasThumbnail = !string.IsNullOrWhiteSpace(ArtUrl),
			Status = Text("PlaybackStatus") switch
			{
				"Playing" => PlaybackState.Playing,
				"Paused" => PlaybackState.Paused,
				_ => PlaybackState.Stopped,
			},
			PlaybackRate = Number(Properties, "Rate"),
			IsShuffleActive = Flag("Shuffle"),
			RepeatMode = loop switch
			{
				"None" => RepeatMode.Off,
				"Track" => RepeatMode.Track,
				"Playlist" => RepeatMode.Context,
				_ => null,
			},
			StartTime = TimeSpan.Zero,
			EndTime = length is > 0 ? TimeSpan.FromMicroseconds(length.Value) : TimeSpan.Zero,
			Position = PositionMicros is > 0 ? TimeSpan.FromMicroseconds(PositionMicros.Value) : TimeSpan.Zero,
			// Position is fetched on every read, so it is current as of now.
			LastUpdatedTime = readAt,
		};
	}

	private IReadOnlyDictionary<string, object?> Metadata =>
		Properties.GetValueOrDefault("Metadata") as IReadOnlyDictionary<string, object?> ?? _empty;

	private static readonly IReadOnlyDictionary<string, object?> _empty = new Dictionary<string, object?>();

	private string? Text(string property) => Properties.GetValueOrDefault(property) as string;

	private bool? Flag(string property) => Properties.GetValueOrDefault(property) as bool?;

	private string? MetadataText(string key) => Metadata.GetValueOrDefault(key) is string { Length: > 0 } text ? text : null;

	// The spec makes artists a list, but some players send a single string.
	private string? MetadataNames(string key) => Metadata.GetValueOrDefault(key) switch
	{
		string { Length: > 0 } name => name,
		object?[] names when names.OfType<string>().Where(name => name.Length > 0).ToList() is { Count: > 0 } named => string.Join(", ", named),
		_ => null,
	};

	// Players disagree on integer widths and some send lengths as doubles.
	private static double? Number(IReadOnlyDictionary<string, object?> values, string key) => values.GetValueOrDefault(key) switch
	{
		long whole => whole,
		double real when double.IsFinite(real) => real,
		_ => null,
	};
}
