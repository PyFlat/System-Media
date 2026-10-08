using NUnit.Framework;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class DesktopEntriesTests
{
	private string _root = null!;

	[SetUp]
	public void CreateDataDirectory() => _root = Directory.CreateTempSubdirectory("system-media-desktop-").FullName;

	[TearDown]
	public void DeleteDataDirectory() => Directory.Delete(_root, recursive: true);

	[Test]
	public void The_icon_named_by_the_desktop_file_is_found_in_the_hicolor_theme()
	{
		Write("applications/org.mozilla.firefox.desktop", "[Desktop Entry]\nName=Firefox\nIcon=firefox-esr\n\n[Desktop Action new-window]\nIcon=other\n");
		var icon = Write("icons/hicolor/128x128/apps/firefox-esr.png", "png");
		Write("icons/hicolor/48x48/apps/firefox-esr.png", "small png");

		Assert.That(new DesktopEntries([_root]).IconPath("org.mozilla.firefox", "firefox"), Is.EqualTo(icon));
	}

	[Test]
	public void Without_a_desktop_entry_the_app_id_names_the_icon()
	{
		var icon = Write("pixmaps/vlc.png", "png");

		Assert.That(new DesktopEntries([_root]).IconPath(desktopEntry: null, "vlc"), Is.EqualTo(icon));
	}

	[Test]
	public void An_absolute_icon_path_is_used_as_it_is_but_only_for_a_png()
	{
		var png = Write("snap/spotify.png", "png");
		Write("applications/spotify.desktop", $"[Desktop Entry]\nIcon={png}\n");
		Write("applications/svg-app.desktop", $"[Desktop Entry]\nIcon={Path.Combine(_root, "snap", "app.svg")}\n");
		Write("snap/app.svg", "<svg/>");

		var entries = new DesktopEntries([_root]);

		Assert.That(entries.IconPath("spotify", "spotify"), Is.EqualTo(png));
		Assert.That(entries.IconPath("svg-app", "svg-app"), Is.Null);
	}

	[Test]
	public void A_desktop_entry_that_is_a_path_is_ignored()
	{
		Write("outside.desktop", "[Desktop Entry]\nIcon=outside\n");
		Write("pixmaps/outside.png", "png");
		var own = Write("pixmaps/player.png", "png");

		Assert.That(new DesktopEntries([_root]).IconPath("../outside", "player"), Is.EqualTo(own));
	}

	[Test]
	public void An_app_without_any_icon_has_none() =>
		Assert.That(new DesktopEntries([_root]).IconPath("unknown", "unknown"), Is.Null);

	[Test]
	public void A_web_app_is_named_by_its_own_desktop_file()
	{
		Write("applications/WebApp-SpotifyWeb3778.desktop", "[Desktop Entry]\nName=Spotify\n\n[Desktop Action new]\nName=Other\n");
		Write("outside.desktop", "[Desktop Entry]\nName=Outside\n");
		var entries = new DesktopEntries([_root]);

		Assert.That(entries.NameOf("WebApp-SpotifyWeb3778"), Is.EqualTo("Spotify"));
		Assert.That(entries.NameOf("../outside"), Is.Null);
		Assert.That(entries.NameOf("unknown"), Is.Null);
	}

	[Test]
	public void The_data_directories_follow_the_xdg_defaults()
	{
		var environment = new Dictionary<string, string?> { ["HOME"] = "/home/me" };

		var directories = DesktopEntries.DataDirectories(name => environment.GetValueOrDefault(name));

		Assert.That(directories[0], Is.EqualTo(Path.Combine("/home/me", ".local", "share")));
		Assert.That(directories[1], Is.EqualTo("/usr/local/share"));
		Assert.That(directories[2], Is.EqualTo("/usr/share"));
		Assert.That(directories, Does.Contain("/var/lib/flatpak/exports/share"));
	}

	[Test]
	public void Configured_data_directories_win_and_relative_ones_are_ignored()
	{
		var environment = new Dictionary<string, string?>
		{
			["HOME"] = "/home/me",
			["XDG_DATA_HOME"] = "/data/home",
			["XDG_DATA_DIRS"] = "/opt/share:relative/share",
		};

		var directories = DesktopEntries.DataDirectories(name => environment.GetValueOrDefault(name));

		Assert.That(directories[0], Is.EqualTo("/data/home"));
		Assert.That(directories[1], Is.EqualTo("/opt/share"));
		Assert.That(directories, Does.Not.Contain("relative/share"));
	}

	private string Write(string relativePath, string content)
	{
		var path = Path.Combine([_root, .. relativePath.Split('/')]);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
		return path;
	}
}
