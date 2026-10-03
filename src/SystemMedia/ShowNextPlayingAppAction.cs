using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace SystemMedia;

internal sealed class ShowNextPlayingAppAction(Func<bool> showNext) : IActionDefinition
{
	internal const string ActionId = "show-next-playing-app";

	public string Id => ActionId;

	public LocalizedText Name => Strings.Actions.ShowNextPlayingApp.Name();

	public LocalizedText Description => Strings.Actions.ShowNextPlayingApp.Description();

	public IReadOnlyList<ActionParameter> Parameters => [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(showNext);

	private sealed class Executor(Func<bool> showNext) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) =>
			showNext()
				? ActionResult.SucceededTask
				: Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.ShowNextPlayingApp.NothingElsePlaying()));
	}
}
