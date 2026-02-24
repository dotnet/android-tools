// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Runs Android Emulator commands.
	/// </summary>
	public class EmulatorRunner
	{
		readonly Func<string?> getSdkPath;

		/// <summary>
		/// Creates a new <see cref="EmulatorRunner"/>.
		/// </summary>
		/// <param name="getSdkPath">Function that returns the Android SDK path.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="getSdkPath"/> is null.</exception>
		public EmulatorRunner (Func<string?> getSdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
		}

		/// <summary>
		/// Gets the path to the emulator executable, or null if not found.
		/// </summary>
		public string? EmulatorPath {
			get {
				var sdkPath = getSdkPath ();
				if (string.IsNullOrEmpty (sdkPath))
					return null;

				var ext = OS.IsWindows ? ".exe" : "";
				var path = Path.Combine (sdkPath, "emulator", "emulator" + ext);

				return File.Exists (path) ? path : null;
			}
		}

		/// <summary>
		/// Gets whether the Android Emulator is available.
		/// </summary>
		public bool IsAvailable => EmulatorPath is not null;

		/// <summary>
		/// Starts an AVD and returns the process.
		/// </summary>
		/// <param name="avdName">The name of the AVD to start.</param>
		/// <param name="coldBoot">Whether to perform a cold boot (ignore snapshots).</param>
		/// <param name="additionalArgs">Additional command-line arguments.</param>
		/// <returns>The emulator process.</returns>
		/// <exception cref="InvalidOperationException">Thrown when Android Emulator is not found.</exception>
		public Process StartAvd (string avdName, bool coldBoot = false, string? additionalArgs = null)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("Android Emulator not found.");

			var args = $"-avd \"{avdName}\"";
			if (coldBoot)
				args += " -no-snapshot-load";
			if (!string.IsNullOrEmpty (additionalArgs))
				args += " " + additionalArgs;

			var psi = new ProcessStartInfo {
				FileName = EmulatorPath!,
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			var process = new Process { StartInfo = psi };
			process.Start ();

			return process;
		}

		/// <summary>
		/// Lists the names of installed AVDs.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of AVD names.</returns>
		/// <exception cref="InvalidOperationException">Thrown when Android Emulator is not found.</exception>
		public async Task<List<string>> ListAvdNamesAsync (CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("Android Emulator not found.");

			var stdout = new StringWriter ();
			var psi = new ProcessStartInfo {
				FileName = EmulatorPath!,
				Arguments = "-list-avds",
				UseShellExecute = false,
				CreateNoWindow = true
			};

			await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

			var avds = new List<string> ();
			foreach (var line in stdout.ToString ().Split ('\n')) {
				var trimmed = line.Trim ();
				if (!string.IsNullOrEmpty (trimmed))
					avds.Add (trimmed);
			}

			return avds;
		}
	}
}
