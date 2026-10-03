using MacroDeck.Sdk.ConfigFlow;
using NUnit.Framework;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class SettingsTests
{
	[Test]
	public void A_widget_that_does_not_cycle_gets_the_plain_player()
	{
		Assert.That(PluginIntegration.CycleSecondsOf(Options(cycle: false, seconds: 10)), Is.Null);
		Assert.That(PluginIntegration.CycleSecondsOf(new Dictionary<string, object>()), Is.Null);
	}

	[Test]
	public void A_cycling_widget_gets_its_interval_within_the_allowed_range()
	{
		Assert.That(PluginIntegration.CycleSecondsOf(Options(cycle: true, seconds: 15.4)), Is.EqualTo(15));
		Assert.That(PluginIntegration.CycleSecondsOf(Options(cycle: true, seconds: 0)), Is.EqualTo(PluginIntegration.MinCycleSeconds));
		Assert.That(PluginIntegration.CycleSecondsOf(Options(cycle: true, seconds: 1e9)), Is.EqualTo(PluginIntegration.MaxCycleSeconds));
		Assert.That(PluginIntegration.CycleSecondsOf(Options(cycle: true, seconds: double.NaN)), Is.EqualTo(10));
	}

	[Test]
	public async Task The_settings_page_has_one_step_that_saves_right_away()
	{
		var flow = new SettingsConfigFlow(exactYouTubeInfo: true);

		var start = await flow.StartAsync(null!, CancellationToken.None);
		Assert.That(start.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
		Assert.That(start.NextStep!.Fields.Select(field => field.Name), Is.EqualTo(new[] { SettingsConfigFlow.ExactYouTubeInfoField }));

		var submitted = await flow.SubmitAsync(start.NextStep.StepId, new Dictionary<string, object?>(), null!, CancellationToken.None);
		Assert.That(submitted.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
	}

	private static Dictionary<string, object> Options(bool cycle, double seconds) => new()
	{
		[PluginIntegration.CycleOption] = cycle,
		[PluginIntegration.CycleSecondsOption] = seconds,
	};
}
