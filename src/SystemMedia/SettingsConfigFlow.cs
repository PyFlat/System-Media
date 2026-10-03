using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace SystemMedia;

// Optional settings: the integration runs on its defaults until this is saved, and a saved entry is read back
// in InitializeAsync, which the host runs again after every save.
internal sealed class SettingsConfigFlow(bool exactYouTubeInfo) : IConfigFlow
{
	internal const string ExactYouTubeInfoField = "exact_youtube_info";

	private const string StepId = "settings";
	private const string EntryTitle = "System Media";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(Step()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken) =>
		Task.FromResult(string.Equals(stepId, StepId, StringComparison.Ordinal)
			? ConfigFlowResult.Complete(EntryTitle)
			: ConfigFlowResult.Error(Step(), Strings.ConfigFlow.Settings.UnknownStep()));

	internal static bool? ReadFlag(string? stored) => bool.TryParse(stored, out var flag) ? flag : null;

	private ConfigFlowStep Step() => new()
	{
		StepId = StepId,
		Title = Strings.ConfigFlow.Settings.Title(),
		Description = Strings.ConfigFlow.Settings.Description(),
		Fields =
		[
			ActionParameter.Toggle(
				ExactYouTubeInfoField,
				Strings.ConfigFlow.Settings.ExactYouTubeInfo.Label(),
				Strings.ConfigFlow.Settings.ExactYouTubeInfo.Description(),
				defaultValue: exactYouTubeInfo),
		],
	};
}
