// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Runs Android Debug Bridge (adb) commands.
	/// </summary>
	public class AdbRunner
	{
		readonly Func<string?> getSdkPath;
		static readonly Regex DetailsRegex = new Regex (@"(\w+):(\S+)", RegexOptions.Compiled);

		/// <summary>
		/// Creates a new <see cref="AdbRunner"/>.
		/// </summary>
		/// <param name="getSdkPath">Function that returns the Android SDK path.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="getSdkPath"/> is null.</exception>
		public AdbRunner (Func<string?> getSdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
		}

		/// <summary>
		/// Gets the path to the adb executable, or null if not found.
		/// </summary>
		public string? AdbPath {
			get {
				var sdkPath = getSdkPath ();
				if (!string.IsNullOrEmpty (sdkPath)) {
					var ext = OS.IsWindows ? ".exe" : "";
					var sdkAdb = Path.Combine (sdkPath, "platform-tools", "adb" + ext);
					if (File.Exists (sdkAdb))
						return sdkAdb;
				}

				return ProcessUtils.FindExecutablesInPath ("adb").FirstOrDefault ();
			}
		}

		/// <summary>
		/// Gets whether ADB is available.
		/// </summary>
		public bool IsAvailable => AdbPath is not null;

		/// <summary>
		/// Lists connected devices.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of connected devices.</returns>
		/// <exception cref="InvalidOperationException">Thrown when ADB is not found.</exception>
		public async Task<List<AdbDeviceInfo>> ListDevicesAsync (CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("ADB not found.");

			var stdout = new StringWriter ();
			var psi = new ProcessStartInfo {
				FileName = AdbPath!,
				Arguments = "devices -l",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

			var devices = new List<AdbDeviceInfo> ();
			foreach (var line in stdout.ToString ().Split ('\n')) {
				var trimmed = line.Trim ();
				if (string.IsNullOrEmpty (trimmed) || trimmed.StartsWith ("List of", StringComparison.OrdinalIgnoreCase))
					continue;

				var parts = trimmed.Split (new [] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length < 2)
					continue;

				var device = new AdbDeviceInfo { Serial = parts [0], State = parts [1] };
				foreach (Match match in DetailsRegex.Matches (trimmed)) {
					switch (match.Groups [1].Value.ToLowerInvariant ()) {
					case "model": device.Model = match.Groups [2].Value; break;
					case "device": device.Device = match.Groups [2].Value; break;
					}
				}
				devices.Add (device);
			}

			return devices;
		}

		/// <summary>
		/// Stops a running emulator.
		/// </summary>
		/// <param name="serial">The emulator serial number (e.g., "emulator-5554").</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <exception cref="InvalidOperationException">Thrown when ADB is not found.</exception>
		public async Task StopEmulatorAsync (string serial, CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("ADB not found.");

			var psi = new ProcessStartInfo {
				FileName = AdbPath!,
				Arguments = $"-s \"{serial}\" emu kill",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			await ProcessUtils.StartProcess (psi, null, null, cancellationToken).ConfigureAwait (false);
		}
	}
}
