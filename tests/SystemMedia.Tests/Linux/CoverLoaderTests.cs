using NUnit.Framework;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class CoverLoaderTests
{
	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

	private string _directory = null!;
	private HttpClient _http = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("system-media-covers-").FullName;
		_http = new HttpClient();
	}

	[TearDown]
	public void TearDown()
	{
		_http.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	[Test]
	public async Task An_image_file_is_loaded()
	{
		await File.WriteAllBytesAsync(Path.Combine(_directory, "cover.png"), _png);

		var cover = await CoverLoader.LoadAsync(new Uri(Path.Combine(_directory, "cover.png")).AbsoluteUri, _http, CancellationToken.None);

		Assert.That(cover?.MimeType, Is.EqualTo("image/png"));
	}

	[Test]
	public async Task A_file_that_is_not_an_image_is_never_passed_on()
	{
		var secret = Path.Combine(_directory, "notes.txt");
		await File.WriteAllTextAsync(secret, "not a picture");

		var cover = await CoverLoader.LoadAsync(new Uri(secret).AbsoluteUri, _http, CancellationToken.None);

		Assert.That(cover, Is.Null);
	}

	[Test]
	public async Task A_data_uri_that_is_not_an_image_is_rejected()
	{
		var cover = await CoverLoader.LoadAsync("data:image/png;base64," + Convert.ToBase64String("text"u8), _http, CancellationToken.None);

		Assert.That(cover, Is.Null);
	}
}
