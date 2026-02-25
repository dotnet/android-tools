// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Security.Principal;
#endif

namespace Xamarin.Android.Tools
{
	public partial class SdkManager
	{
		static void SetExecutablePermissions (string directory, Action<TraceLevel, string> logger)
		{
			// Make sdkmanager and other binaries executable on Unix
			var binDir = Path.Combine (directory, "bin");
			if (Directory.Exists (binDir)) {
				foreach (var file in Directory.GetFiles (binDir)) {
					// Use p/invoke chmod for efficiency (avoid spawning processes)
					if (!Chmod (file, 0x1ED)) { // 0755 octal = 0x1ED
						// Fallback to chmod process if p/invoke fails
						try {
							var psi = new ProcessStartInfo ("chmod", $"+x \"{file}\"") {
								CreateNoWindow = true,
								UseShellExecute = false,
							};
							using var process = Process.Start (psi);
							process?.WaitForExit ();
							if (process is null || process.ExitCode != 0) {
								throw new InvalidOperationException ($"chmod failed for '{file}' with exit code {process?.ExitCode ?? -1}");
							}
						}
						catch (Exception ex) {
							// Let the exception propagate - sdkmanager won't work without executable permissions
							logger (TraceLevel.Error, $"Failed to set executable permission on '{file}': {ex.Message}");
							throw new InvalidOperationException ($"Failed to set executable permissions on '{file}'. The sdkmanager will not be usable.", ex);
						}
					}
				}
			}
		}

		/// <summary>
		/// Sets file permissions on Unix using libc chmod.
		/// </summary>
		/// <param name="path">File path.</param>
		/// <param name="mode">Permission mode (e.g., 0x1ED for 0755 octal).</param>
		/// <returns>True if successful, false otherwise.</returns>
		static bool Chmod (string path, int mode)
		{
			try {
				return chmod (path, mode) == 0;
			}
			catch {
				// p/invoke failed (e.g., not on Unix) - caller will use fallback
				return false;
			}
		}

		[DllImport ("libc", SetLastError = true)]
		static extern int chmod (string pathname, int mode);

		[DllImport ("libc")]
		static extern uint geteuid ();

		static void CopyDirectoryRecursive (string sourceDir, string destinationDir)
		{
			if (!Directory.Exists (destinationDir))
				Directory.CreateDirectory (destinationDir);

			foreach (var file in Directory.GetFiles (sourceDir)) {
				var destFile = Path.Combine (destinationDir, Path.GetFileName (file));
				File.Copy (file, destFile, overwrite: true);
			}

			foreach (var subDir in Directory.GetDirectories (sourceDir)) {
				var destSubDir = Path.Combine (destinationDir, Path.GetFileName (subDir));
				CopyDirectoryRecursive (subDir, destSubDir);
			}
		}

		/// <summary>
		/// Determines whether the current process is running with elevated privileges
		/// (Administrator on Windows, root on macOS/Linux).
		/// </summary>
		static bool IsCurrentProcessElevated ()
		{
#if NET5_0_OR_GREATER
			if (OS.IsWindows) {
#pragma warning disable CA1416
				using var identity = WindowsIdentity.GetCurrent ();
				var principal = new WindowsPrincipal (identity);
				return principal.IsInRole (WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
			}
#endif
			try {
				return geteuid () == 0;
			}
			catch {
				return false;
			}
		}
	}
}
