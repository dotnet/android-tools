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

		public AdbRunner (Func<string?> getSdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
		}

		internal string? AdbPath {
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

		public bool IsAvailable => AdbPath is not null;

		string RequireAdb ()
		{
			return AdbPath ?? throw new InvalidOperationException ("ADB not found.");
		}

		ProcessStartInfo CreateAdbProcess (string adbPath, params string [] args)
		{
			var psi = new ProcessStartInfo {
				FileName = adbPath,
				Arguments = string.Join (" ", args),
				UseShellExecute = false,
				CreateNoWindow = true
			};

			var sdkPath = getSdkPath ();
			if (!string.IsNullOrEmpty (sdkPath))
				psi.EnvironmentVariables ["ANDROID_HOME"] = sdkPath;

			return psi;
		}

		public async Task<List<AdbDeviceInfo>> ListDevicesAsync (CancellationToken cancellationToken = default)
		{
			var adb = RequireAdb ();
			var stdout = new StringWriter ();
			var psi = CreateAdbProcess (adb, "devices", "-l");
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

		public async Task WaitForDeviceAsync (string? serial = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
		{
			var adb = RequireAdb ();
			var effectiveTimeout = timeout ?? TimeSpan.FromSeconds (60);

			var args = string.IsNullOrEmpty (serial)
				? new [] { "wait-for-device" }
				: new [] { "-s", serial, "wait-for-device" };

			var psi = CreateAdbProcess (adb, args);

			using var cts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			cts.CancelAfter (effectiveTimeout);

			try {
				await ProcessUtils.StartProcess (psi, null, null, cts.Token).ConfigureAwait (false);
			} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
				throw new TimeoutException ($"Timed out waiting for device after {effectiveTimeout.TotalSeconds}s.");
			}
		}

		public async Task StopEmulatorAsync (string serial, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace (serial))
				throw new ArgumentException ("Serial must not be empty.", nameof (serial));

			var adb = RequireAdb ();
			var psi = CreateAdbProcess (adb, "-s", serial, "emu", "kill");
			await ProcessUtils.StartProcess (psi, null, null, cancellationToken).ConfigureAwait (false);
		}
	}
}

