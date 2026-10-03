using System.Runtime.InteropServices;
using SystemMedia.Core;

namespace SystemMedia.Platform.Windows.Browser;

// The first read switches on the browser's accessibility support (and can come back incomplete), so this
// only runs for the opt-in exact YouTube info.
internal sealed class BrowserTabs : IDisposable
{
	private static readonly TimeSpan _reuseFor = TimeSpan.FromSeconds(1);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeProvider _time;
	private readonly Dictionary<BrowserUi, (DateTimeOffset ReadAt, IReadOnlyList<BrowserTab> Tabs)> _last = [];

	internal BrowserTabs(TimeProvider time) => _time = time;

	internal async Task<IReadOnlyList<BrowserTab>> ReadAsync(BrowserUi browser, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_last.TryGetValue(browser, out var last) && _time.GetUtcNow() - last.ReadAt < _reuseFor)
			{
				return last.Tabs;
			}

			var tabs = await Task.Run(() => ReadNow(browser), cancellationToken);
			_last[browser] = (_time.GetUtcNow(), tabs);
			return tabs;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	private static List<BrowserTab> ReadNow(BrowserUi browser)
	{
		var windows = BrowserWindows.Find(browser);
		if (windows.Count == 0)
		{
			return [];
		}

		var tabs = new List<BrowserTab>();
		var com = new ComObjects();
		try
		{
			var automation = com.Keep((IUIAutomation)new CUIAutomationComObject());
			var tabCondition = com.Keep(Condition(automation, browser.Tab));
			var soundCondition = com.Keep(Condition(automation, browser.PlayingIndicator));
			var addressCondition = com.Keep(Condition(automation, browser.AddressBar));

			foreach (var (handle, windowTitle) in windows)
			{
				var before = tabs.Count;
				ReadWindow(com, automation, handle, tabCondition, soundCondition, addressCondition, tabs);

				// A fullscreen window has no tab strip, but its title still names the tab in front.
				if (tabs.Count == before)
				{
					tabs.Add(new BrowserTab(windowTitle, null, false));
				}
			}
		}
		catch (COMException)
		{
			// A window closed while it was being read; what was read so far is still true.
		}
		finally
		{
			com.ReleaseAll();
		}

		return tabs;
	}

	private static void ReadWindow(
		ComObjects com,
		IUIAutomation automation,
		IntPtr handle,
		IUIAutomationCondition tabCondition,
		IUIAutomationCondition soundCondition,
		IUIAutomationCondition addressCondition,
		List<BrowserTab> tabs)
	{
		if (automation.ElementFromHandle(handle, out var window) < 0 || window is null)
		{
			return;
		}

		com.Keep(window);
		var address = window.FindFirst(UiaTreeScope.Descendants, addressCondition, out var addressBar) >= 0 && addressBar is not null
			? Property(com.Keep(addressBar), UiaProperty.ValueValue) as string
			: null;

		if (window.FindAll(UiaTreeScope.Descendants, tabCondition, out var found) < 0 || found is null)
		{
			return;
		}

		com.Keep(found);
		_ = found.GetLength(out var count);
		for (var index = 0; index < count; index++)
		{
			if (found.GetElement(index, out var tab) < 0 || tab is null)
			{
				continue;
			}

			com.Keep(tab);
			if (Property(tab, UiaProperty.Name) is not string { Length: > 0 } title)
			{
				continue;
			}

			var inFront = Property(tab, UiaProperty.SelectionItemIsSelected) is true;
			var playing = tab.FindFirst(UiaTreeScope.Descendants, soundCondition, out var soundButton) >= 0 && soundButton is not null;
			if (soundButton is not null)
			{
				com.Keep(soundButton);
			}

			tabs.Add(new BrowserTab(title, inFront ? address : null, playing));
		}
	}

	private static IUIAutomationCondition Condition(IUIAutomation automation, UiaMatch match)
	{
		Marshal.ThrowExceptionForHR(automation.CreatePropertyCondition(match.PropertyId, match.Value, out var condition));
		return condition;
	}

	private static object? Property(IUIAutomationElement element, int propertyId) =>
		element.GetCurrentPropertyValue(propertyId, out var value) >= 0 ? value : null;

	private sealed class ComObjects
	{
		// One COM object can come back as the same wrapper more than once, and must be released only once.
		private readonly HashSet<object> _objects = new(ReferenceEqualityComparer.Instance);

		public T Keep<T>(T value)
			where T : class
		{
			_objects.Add(value);
			return value;
		}

		public void ReleaseAll()
		{
			foreach (var value in _objects)
			{
				Marshal.ReleaseComObject(value);
			}

			_objects.Clear();
		}
	}
}
