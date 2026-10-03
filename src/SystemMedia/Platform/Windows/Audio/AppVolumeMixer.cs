using System.Runtime.InteropServices;
using SystemMedia.Core;

namespace SystemMedia.Platform.Windows.Audio;

// The transport controls have no volume, so this is the volume mixer's. Browsers play from several helper
// processes: every match is set and the loudest is reported.
internal static class AppVolumeMixer
{
	internal static int? GetVolumePercent(AppIdentity app)
	{
		float? loudest = null;
		ForEachSession(app, volume =>
		{
			if (volume.GetMasterVolume(out var level) == 0)
			{
				loudest = Math.Max(loudest ?? 0, level);
			}
		});

		return loudest is { } value ? (int)Math.Round(value * 100) : null;
	}

	internal static bool SetVolumePercent(AppIdentity app, int volumePercent)
	{
		var level = Math.Clamp(volumePercent, 0, 100) / 100f;
		var context = Guid.Empty;
		var applied = false;
		ForEachSession(app, volume => applied |= volume.SetMasterVolume(level, ref context) == 0);
		return applied;
	}

	private static void ForEachSession(AppIdentity app, Action<ISimpleAudioVolume> visit)
	{
		var executable = app.ExecutableFileName;
		var packageFamily = app.PackageFamilyName;
		if (executable is null && packageFamily is null)
		{
			return;
		}

		var processes = new Dictionary<uint, bool>();
		bool Owns(uint processId)
		{
			if (!processes.TryGetValue(processId, out var owned))
			{
				var (processExecutable, processPackageFamily) = ProcessInfo.Read(processId);
				owned = (packageFamily is not null && string.Equals(packageFamily, processPackageFamily, StringComparison.OrdinalIgnoreCase))
					|| (executable is not null && string.Equals(executable, processExecutable, StringComparison.OrdinalIgnoreCase));
				processes[processId] = owned;
			}

			return owned;
		}

		try
		{
			ForEachRenderSession((processId, volume) =>
			{
				if (processId != 0 && Owns(processId))
				{
					visit(volume);
				}
			});
		}
		catch (COMException)
		{
			// No audio service or no output device: no volume to report.
		}
	}

	// An app can be routed to a device other than the default one.
	private static void ForEachRenderSession(Action<uint, ISimpleAudioVolume> visit)
	{
		var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
		IMMDeviceCollection? devices = null;
		try
		{
			if (enumerator.EnumAudioEndpoints(CoreAudioNative.DataFlowRender, CoreAudioNative.DeviceStateActive, out devices) != 0 ||
				devices.GetCount(out var deviceCount) != 0)
			{
				return;
			}

			for (var i = 0; i < deviceCount; i++)
			{
				if (devices.Item(i, out var device) != 0)
				{
					continue;
				}

				try
				{
					VisitDeviceSessions(device, visit);
				}
				finally
				{
					Marshal.ReleaseComObject(device);
				}
			}
		}
		finally
		{
			if (devices is not null)
			{
				Marshal.ReleaseComObject(devices);
			}

			Marshal.ReleaseComObject(enumerator);
		}
	}

	private static void VisitDeviceSessions(IMMDevice device, Action<uint, ISimpleAudioVolume> visit)
	{
		var managerId = typeof(IAudioSessionManager2).GUID;
		if (device.Activate(ref managerId, CoreAudioNative.ClassContextAll, IntPtr.Zero, out var activated) != 0)
		{
			return;
		}

		var manager = (IAudioSessionManager2)activated;
		IAudioSessionEnumerator? sessions = null;
		try
		{
			if (manager.GetSessionEnumerator(out sessions) != 0 || sessions.GetCount(out var sessionCount) != 0)
			{
				return;
			}

			for (var i = 0; i < sessionCount; i++)
			{
				if (sessions.GetSession(i, out var session) != 0)
				{
					continue;
				}

				try
				{
					if (session.GetState(out var state) == 0 && state != CoreAudioNative.SessionStateExpired &&
						session.GetProcessId(out var processId) == 0 &&
						session is ISimpleAudioVolume volume)
					{
						visit(processId, volume);
					}
				}
				finally
				{
					Marshal.ReleaseComObject(session);
				}
			}
		}
		finally
		{
			if (sessions is not null)
			{
				Marshal.ReleaseComObject(sessions);
			}

			Marshal.ReleaseComObject(manager);
		}
	}
}
