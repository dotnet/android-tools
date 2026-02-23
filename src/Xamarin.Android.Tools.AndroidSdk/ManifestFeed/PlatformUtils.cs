using System.Runtime.InteropServices;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Utility class for platform detection and normalization.
	/// </summary>
	internal static class PlatformUtils
	{
		/// <summary>
		/// Gets the current operating system name in manifest format.
		/// </summary>
		public static string GetCurrentOsName ()
		{
			if (OS.IsWindows)
				return "windows";
			if (OS.IsMac)
				return "macosx";
			return "linux";
		}

		/// <summary>
		/// Gets the current architecture name in manifest format.
		/// </summary>
		public static string GetCurrentArchName ()
		{
			var arch = RuntimeInformation.OSArchitecture;
			switch (arch) {
				case Architecture.X64:
					return "x86_64";
				case Architecture.Arm64:
					return "aarch64";
				case Architecture.X86:
					return "x86";
				case Architecture.Arm:
					return "arm";
				default:
					return "x86_64";
			}
		}

		/// <summary>
		/// Normalizes an OS name to a standard manifest format.
		/// </summary>
		public static string? NormalizeOsName (string? os)
		{
			if (string.IsNullOrEmpty (os))
				return os;

			switch (os.ToLowerInvariant ()) {
				case "windows":
				case "win":
					return "windows";
				case "macosx":
				case "macos":
				case "darwin":
				case "osx":
					return "macosx";
				case "linux":
					return "linux";
				default:
					return os.ToLowerInvariant ();
			}
		}

		/// <summary>
		/// Normalizes an architecture name to a standard manifest format.
		/// </summary>
		public static string? NormalizeArchName (string? arch)
		{
			if (string.IsNullOrEmpty (arch))
				return arch;

			switch (arch.ToLowerInvariant ()) {
				case "x86_64":
				case "x64":
				case "amd64":
					return "x86_64";
				case "aarch64":
				case "arm64":
					return "aarch64";
				case "x86":
				case "i386":
				case "i686":
					return "x86";
				default:
					return arch.ToLowerInvariant ();
			}
		}
	}
}
