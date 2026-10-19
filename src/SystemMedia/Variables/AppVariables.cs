using MacroDeck.Sdk.Variables;

namespace SystemMedia.Variables;

// Every known app's own copy of the state variables, so a template can follow one player whatever else plays.
internal sealed class AppVariables
{
	// The host drops every eager variable past its limit, so the apps that would not fit get none.
	internal static int MaxApps { get; } =
		(VariableLimits.MaxEagerVariablesPerProvider - SystemMediaVariables.All.Count) / SystemMediaVariables.PerApp;

	private readonly Dictionary<string, Target> _targets;

	internal AppVariables(IEnumerable<(string AppId, string AppName)> apps)
	{
		var definitions = new List<VariableDefinition>();
		_targets = new Dictionary<string, Target>(StringComparer.Ordinal);
		foreach (var (appId, appName) in apps.Take(MaxApps))
		{
			foreach (var (definition, currentId) in SystemMediaVariables.ForApp(appId, appName))
			{
				definitions.Add(definition);
				_targets[definition.ResolvedId!] = new Target(appId, currentId);
			}
		}

		Definitions = definitions;
	}

	internal static AppVariables None { get; } = new([]);

	internal IReadOnlyList<VariableDefinition> Definitions { get; }

	internal Target? Find(string localId) => _targets.GetValueOrDefault(localId);

	internal sealed record Target(string AppId, string CurrentId);
}
