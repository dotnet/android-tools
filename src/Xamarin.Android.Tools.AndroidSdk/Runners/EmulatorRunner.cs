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
		readonly Func<string?>? getJdkPath;

		public EmulatorRunner (Func<string?> getSdkPath)
			: this (getSdkPath, null)
		{
		}

		public EmulatorRunner (Func<string?> getSdkPath, Func<string?>? getJdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
			this.getJdkPath = getJdkPath;
		}

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

		public bool IsAvailable => EmulatorPath is not null;

		void ConfigureEnvironment (ProcessStartInfo psi)
		{
			var sdkPath = getSdkPath ();
			if (!string.IsNullOrEmpty (sdkPath))
				psi.EnvironmentVariables ["ANDROID_HOME"] = sdkPath;

			var jdkPath = getJdkPath?.Invoke ();
			if (!string.IsNullOrEmpty (jdkPath))
				psi.EnvironmentVariables ["JAVA_HOME"] = jdkPath;
		}

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
			ConfigureEnvironment (psi);

			var process = new Process { StartInfo = psi };
			process.Start ();

			return process;
		}

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
			ConfigureEnvironment (psi);

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

