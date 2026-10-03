using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using SystemMedia;

var builder = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<PluginIntegration>();

var plugin = builder.Build();

await plugin.RunAsync();
