using System.Runtime.InteropServices;

namespace SystemMedia.Platform.Windows.Browser;

// UIAutomationClient.h through COM, since System.Windows.Automation needs the Windows Desktop runtime. COM
// binds by vtable position, so every method up to the last one called is declared in header order.

[ComImport]
[Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")]
internal class CUIAutomationComObject;

internal static class UiaTreeScope
{
	internal const int Descendants = 4;
}

internal static class UiaProperty
{
	internal const int Name = 30005;
	internal const int AutomationId = 30011;
	internal const int ClassName = 30012;
	internal const int ValueValue = 30045;
	internal const int SelectionItemIsSelected = 30079;
}

[ComImport]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
	[PreserveSig] int CompareElements(IntPtr element1, IntPtr element2, out int areSame);
	[PreserveSig] int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, out int areSame);
	[PreserveSig] int GetRootElement(out IntPtr root);
	[PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);
	[PreserveSig] int ElementFromPoint(long point, out IntPtr element);
	[PreserveSig] int GetFocusedElement(out IntPtr element);
	[PreserveSig] int GetRootElementBuildCache(IntPtr cacheRequest, out IntPtr root);
	[PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, out IntPtr element);
	[PreserveSig] int ElementFromPointBuildCache(long point, IntPtr cacheRequest, out IntPtr element);
	[PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest, out IntPtr element);
	[PreserveSig] int CreateTreeWalker(IntPtr condition, out IntPtr walker);
	[PreserveSig] int GetControlViewWalker(out IntPtr walker);
	[PreserveSig] int GetContentViewWalker(out IntPtr walker);
	[PreserveSig] int GetRawViewWalker(out IntPtr walker);
	[PreserveSig] int GetRawViewCondition(out IntPtr condition);
	[PreserveSig] int GetControlViewCondition(out IntPtr condition);
	[PreserveSig] int GetContentViewCondition(out IntPtr condition);
	[PreserveSig] int CreateCacheRequest(out IntPtr cacheRequest);
	[PreserveSig] int CreateTrueCondition(out IntPtr condition);
	[PreserveSig] int CreateFalseCondition(out IntPtr condition);
	[PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, out IUIAutomationCondition condition);
}

[ComImport]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition;

[ComImport]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
	[PreserveSig] int SetFocus();
	[PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
	[PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition, out IUIAutomationElement? found);
	[PreserveSig] int FindAll(int scope, IUIAutomationCondition condition, out IUIAutomationElementArray? found);
	[PreserveSig] int FindFirstBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, out IntPtr found);
	[PreserveSig] int FindAllBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, out IntPtr found);
	[PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, out IntPtr updated);
	[PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
}

[ComImport]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
	[PreserveSig] int GetLength(out int length);
	[PreserveSig] int GetElement(int index, out IUIAutomationElement element);
}
