using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Testing;
using NUnit.Framework;

namespace SystemMedia.Tests;

// Runs against the machine's real media sessions, so only asserts what holds whether or not something plays.
[TestFixture]
public sealed class PluginIntegrationTests
{
	private static readonly string[] _eventIds = ["track-changed", "playback-state-changed", "current-app-changed"];
	private static readonly string[] _transportActions = ["play", "pause", "toggle-play-pause", "next", "previous", "seek", "set-volume"];

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness();

		await Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task The_first_instance_follows_whatever_is_playing()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var instances = (await harness.MusicPlayer.GetInstancesAsync()).DataAs<MusicPlayerInstancesResult>();

		// The host resolves a widget with no instance picked to the first one listed.
		Assert.That(instances!.Instances[0].Id, Is.EqualTo("current"));
	}

	[Test]
	public async Task The_settings_page_is_optional_and_keeps_every_player_listed()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var flow = (await harness.ConfigFlow.DescribeAsync()).DataAs<ConfigFlowDescribePayload>();

		Assert.That(flow!.RequiresConfiguration, Is.False);
		Assert.That(flow.AllowsMultipleConfigurations, Is.True);
	}

	[Test]
	public async Task Every_instance_name_says_which_plugin_it_comes_from()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var instances = (await harness.MusicPlayer.GetInstancesAsync()).DataAs<MusicPlayerInstancesResult>();

		Assert.That(instances!.Instances[0].DisplayName, Is.EqualTo("System Media - Any app"));
		Assert.That(instances.Instances.Select(instance => instance.DisplayName), Has.All.StartsWith("System Media - "));
	}

	[Test]
	public async Task Every_instance_id_is_a_valid_local_id()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var instances = (await harness.MusicPlayer.GetInstancesAsync()).DataAs<MusicPlayerInstancesResult>();

		Assert.That(instances!.Instances.Select(instance => instance.Id), Has.All.Matches(InstanceIdsTests.LocalIdPattern));
	}

	[Test]
	public async Task The_standard_transport_actions_are_declared()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var catalog = (await harness.Actions.DescribeAsync()).DataAs<ActionCatalogPayload>();

		Assert.That(catalog!.Actions.Select(action => action.LocalId), Is.SupersetOf(_transportActions));
	}

	[Test]
	public async Task Track_state_and_app_changes_are_declared_as_events_each_narrowable_to_one_app()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var catalog = (await harness.Events.DescribeAsync()).DataAs<EventCatalogPayload>();

		Assert.That(catalog!.Events.Select(e => e.LocalId), Is.EquivalentTo(_eventIds));
		foreach (var definition in catalog.Events)
		{
			var app = definition.ConfigurationParameters.Single(p => p.Name == "app");
			Assert.That(app.Type, Is.EqualTo("DynamicChoice"));
			Assert.That(app.Required, Is.False);
			Assert.That(definition.PayloadParameters.Select(p => p.Name), Does.Contain("app").And.Contain("appName"));
		}

		Assert.That(catalog.HasDynamicEventOptions, Is.True);
	}

	internal static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder => builder
			.UseLocalization(Strings.LocalizationCatalog)
			.RegisterIntegration<PluginIntegration>());
}

[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.pyflat.system-media"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
			Assert.That(text, Is.Not.Empty);
		}
	}
}
