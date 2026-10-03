using NUnit.Framework;
using SystemMedia.Platform.Windows.Vlc;

namespace SystemMedia.Tests.Windows;

[TestFixture]
public sealed class VlcSetupTests
{
	private const string Defaults = "[qt]\r\n#qt-start-minimized=0\r\n\r\n[core]\r\n#extraintf=\r\n#control=\r\n#verbose=0\r\n";

	[Test]
	public void A_default_config_is_not_enabled_and_gets_the_default_line_uncommented()
	{
		var enabled = VlcSetup.WithAddOnEnabled(Defaults);

		Assert.That(VlcSetup.IsEnabled(Defaults), Is.False);
		Assert.That(enabled, Is.EqualTo(Defaults.Replace("#control=\r\n", "control=win10smtc\r\n", StringComparison.Ordinal)));
		Assert.That(VlcSetup.IsEnabled(enabled), Is.True);
	}

	[Test]
	public void Another_control_interface_already_set_is_kept()
	{
		var settings = Defaults.Replace("#control=", "control=hotkeys", StringComparison.Ordinal);

		var enabled = VlcSetup.WithAddOnEnabled(settings);

		Assert.That(enabled, Does.Contain("control=hotkeys:win10smtc\r\n"));
		Assert.That(VlcSetup.IsEnabled(enabled), Is.True);
	}

	[Test]
	public void A_config_without_the_line_gets_it_under_core()
	{
		var enabled = VlcSetup.WithAddOnEnabled("[qt]\n#a=1\n[core]\n#verbose=0\n");

		Assert.That(enabled, Is.EqualTo("[qt]\n#a=1\n[core]\ncontrol=win10smtc\n#verbose=0\n"));
	}

	[Test]
	public void A_missing_config_becomes_a_core_section_with_the_line()
	{
		Assert.That(VlcSetup.WithAddOnEnabled(string.Empty), Is.EqualTo("[core]\ncontrol=win10smtc\n"));
	}

	[TestCase("control=win10smtc", true)]
	[TestCase("control=hotkeys:win10smtc", true)]
	[TestCase("#control=win10smtc", false)]
	[TestCase("control=", false)]
	[TestCase("extraintf=win10smtc", false)]
	public void Only_an_active_control_line_naming_the_add_on_counts(string line, bool expected)
	{
		Assert.That(VlcSetup.IsEnabled("[core]\n" + line + "\n"), Is.EqualTo(expected));
	}
}
