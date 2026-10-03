namespace SystemMedia.Platform.Windows.Browser;

internal readonly record struct UiaMatch(int PropertyId, string Value);

// Internal UI names, independent of the user's language. Chromium's playing indicator also shows for a
// tab using the camera or microphone.
internal sealed record BrowserUi(string ExecutableFileName, string WindowClass, UiaMatch Tab, UiaMatch PlayingIndicator, UiaMatch AddressBar)
{
	internal static readonly BrowserUi Firefox = new(
		"firefox.exe",
		"MozillaWindowClass",
		new(UiaProperty.ClassName, "tabbrowser-tab"),
		new(UiaProperty.AutomationId, "main-button"),
		new(UiaProperty.AutomationId, "urlbar-input"));

	internal static readonly BrowserUi Chrome = new(
		"chrome.exe",
		"Chrome_WidgetWin_1",
		new(UiaProperty.ClassName, "Tab"),
		new(UiaProperty.ClassName, "AlertIndicatorButton"),
		new(UiaProperty.ClassName, "OmniboxViewViews"));

	internal static readonly BrowserUi Edge = Chrome with { ExecutableFileName = "msedge.exe", Tab = new(UiaProperty.ClassName, "EdgeTab") };

	internal static readonly IReadOnlyList<BrowserUi> All = [Firefox, Chrome, Edge];
}
