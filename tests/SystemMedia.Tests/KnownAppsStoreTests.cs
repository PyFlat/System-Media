using NUnit.Framework;
using Serilog;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class KnownAppsStoreTests
{
	private static readonly string[] _legacyIds = ["vlc.exe", "308046B0AF4A39CB"];

	[Test]
	public void The_plain_id_list_version_1_0_0_wrote_is_still_read()
	{
		var apps = KnownAppsStore.Parse("""["vlc.exe","308046B0AF4A39CB"]""");

		Assert.That(apps.Select(app => app.AppId), Is.EqualTo(_legacyIds));
		Assert.That(apps.Select(app => app.Name), Has.All.Null);
	}

	[Test]
	public void Names_survive_a_save_and_load()
	{
		var directory = Path.Combine(Path.GetTempPath(), "system-media-tests-" + Guid.NewGuid().ToString("N"));
		try
		{
			var store = new KnownAppsStore(directory, new LoggerConfiguration().CreateLogger());
			store.Save([new RememberedApp("vlc.exe", "VLC media player")]);

			Assert.That(store.Load(), Is.EqualTo(new[] { new RememberedApp("vlc.exe", "VLC media player") }));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void An_unreadable_file_is_treated_as_no_remembered_apps()
	{
		Assert.That(KnownAppsStore.Parse("""{"not":"a list"}"""), Is.Empty);
	}
}
