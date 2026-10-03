using Windows.Foundation;

namespace SystemMedia.Platform.Windows;

// Some WinRT operations complete on another app's STA thread. Continuing there binds every session fetched
// afterwards to it, and they fail with RPC_E_WRONG_THREAD.
internal static class WinRtAwait
{
	internal static async Task<T> OnThreadPool<T>(this IAsyncOperation<T> operation, CancellationToken cancellationToken = default) =>
		await operation.AsTask(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

	internal static async Task OnThreadPool(this IAsyncAction action, CancellationToken cancellationToken = default) =>
		await action.AsTask(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

	internal static async Task OnThreadPool(this Task task) =>
		await task.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
}
