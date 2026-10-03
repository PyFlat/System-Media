using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace SystemMedia.Platform.Windows;

// Keyed by where the icon comes from, not the app id, so an app first seen before its executable was known still gets one.
internal sealed class AppIconCache
{
	private const int IconSize = 256;

	private readonly ConcurrentDictionary<string, Lazy<Task<MusicPlayerArtwork?>>> _icons = new(StringComparer.OrdinalIgnoreCase);

	internal static bool HasIconSource(AppIdentity app) => app.PackageFamilyName is not null || app.ExecutablePath is not null;

	internal Task<MusicPlayerArtwork?> GetAsync(AppIdentity app, CancellationToken cancellationToken)
	{
		var source = app.PackageFamilyName is not null ? "pkg:" + app.AppId
			: app.ExecutablePath is { } path ? "exe:" + path
			: null;
		if (source is null)
		{
			return Task.FromResult<MusicPlayerArtwork?>(null);
		}

		// Shared by every caller and never cancelled by one of them; each caller only stops waiting.
		var load = _icons.GetOrAdd(source, _ => new Lazy<Task<MusicPlayerArtwork?>>(() => LoadAsync(app)));
		return load.Value.WaitAsync(cancellationToken);
	}

	private static async Task<MusicPlayerArtwork?> LoadAsync(AppIdentity app)
	{
		try
		{
			return app.PackageFamilyName is not null
				? await LoadPackagedLogoAsync(app.AppId)
				: await LoadExecutableIconAsync(app.ExecutablePath!);
		}
		catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or ArgumentException)
		{
			return null;
		}
	}

	private static async Task<MusicPlayerArtwork?> LoadPackagedLogoAsync(string appUserModelId)
	{
		var logo = AppInfo.GetFromAppUserModelId(appUserModelId).DisplayInfo.GetLogo(new global::Windows.Foundation.Size(IconSize, IconSize));
		using var stream = await logo.OpenReadAsync().OnThreadPool();
		var reportedType = stream.ContentType;
		var bytes = await ReadAllAsync(stream);
		return bytes.Length == 0 ? null : new MusicPlayerArtwork(bytes, ArtworkFormat.ContentTypeOrSniffed(reportedType, bytes));
	}

	// Re-encoded as PNG to keep the icon's transparent edges.
	private static async Task<MusicPlayerArtwork?> LoadExecutableIconAsync(string executablePath)
	{
		var file = await StorageFile.GetFileFromPathAsync(executablePath).OnThreadPool();
		using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, IconSize, ThumbnailOptions.ResizeThumbnail).OnThreadPool();
		if (thumbnail is null)
		{
			return null;
		}

		var decoder = await BitmapDecoder.CreateAsync(thumbnail).OnThreadPool();
		using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).OnThreadPool();
		using var png = new InMemoryRandomAccessStream();
		var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png).OnThreadPool();
		encoder.SetSoftwareBitmap(bitmap);
		await encoder.FlushAsync().OnThreadPool();
		png.Seek(0);
		var bytes = await ReadAllAsync(png);
		return bytes.Length == 0 ? null : new MusicPlayerArtwork(bytes, "image/png");
	}

	private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
	{
		await using var source = stream.AsStreamForRead();
		using var buffer = new MemoryStream();
		await source.CopyToAsync(buffer).OnThreadPool();
		return buffer.ToArray();
	}
}
