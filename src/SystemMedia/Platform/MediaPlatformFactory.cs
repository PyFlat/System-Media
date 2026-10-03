using Serilog;
#if WINDOWS
using SystemMedia.Platform.Windows;
#else
using SystemMedia.Platform.Linux;
using SystemMedia.Platform.MacOS;
#endif

namespace SystemMedia.Platform;

internal static class MediaPlatformFactory
{
	internal static IMediaPlatform Create(ILogger logger)
	{
#if DEBUG
		if (Environment.GetEnvironmentVariable(Demo.DemoMediaPlatform.EnvironmentVariable) is { Length: > 0 } demoFolder)
		{
			logger.Warning("Demo mode: showing made-up sessions from {Folder} instead of the system's.", demoFolder);
			return new Demo.DemoMediaPlatform(demoFolder);
		}
#endif

#if WINDOWS
		return new WindowsMediaPlatform(logger);
#else
		if (OperatingSystem.IsMacOS())
		{
			return new MacMediaPlatform(logger);
		}

		return OperatingSystem.IsLinux() ? new LinuxMediaPlatform(logger) : new UnsupportedMediaPlatform();
#endif
	}
}
