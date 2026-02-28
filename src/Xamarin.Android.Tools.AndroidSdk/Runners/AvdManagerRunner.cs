// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Runs Android Virtual Device Manager (avdmanager) commands.
	/// </summary>
	public class AvdManagerRunner
	{
		readonly Func<string?> getSdkPath;
		readonly Func<string?>? getJdkPath;

		public AvdManagerRunner (Func<string?> getSdkPath)
			: this (getSdkPath, null)
		{
		}

		public AvdManagerRunner (Func<string?> getSdkPath, Func<string?>? getJdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
			this.getJdkPath = getJdkPath;
		}

		public string? AvdManagerPath {
			get {
				var sdkPath = getSdkPath ();
				if (string.IsNullOrEmpty (sdkPath))
					return null;

				var ext = OS.IsWindows ? ".bat" : "";
				var cmdlineToolsPath = Path.Combine (sdkPath, "cmdline-tools", "latest", "bin", "avdmanager" + ext);
				if (File.Exists (cmdlineToolsPath))
					return cmdlineToolsPath;

				var toolsPath = Path.Combine (sdkPath, "tools", "bin", "avdmanager" + ext);

				return File.Exists (toolsPath) ? toolsPath : null;
			}
		}

		public bool IsAvailable => !string.IsNullOrEmpty (AvdManagerPath);

		void ConfigureEnvironment (ProcessStartInfo psi)
		{
			var sdkPath = getSdkPath ();
			if (!string.IsNullOrEmpty (sdkPath))
				psi.EnvironmentVariables ["ANDROID_HOME"] = sdkPath;

			var jdkPath = getJdkPath?.Invoke ();
			if (!string.IsNullOrEmpty (jdkPath))
				psi.EnvironmentVariables ["JAVA_HOME"] = jdkPath;
		}

		public async Task<List<AvdInfo>> ListAvdsAsync (CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("AVD Manager not found.");

			var stdout = new StringWriter ();
			var psi = new ProcessStartInfo {
				FileName = AvdManagerPath!,
				Arguments = "list avd",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			ConfigureEnvironment (psi);
			await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

			return ParseAvdListOutput (stdout.ToString ());
		}

		public async Task<AvdInfo> CreateAvdAsync (string name, string systemImage, string? deviceProfile = null,
			bool force = false, CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("AVD Manager not found.");
			if (string.IsNullOrEmpty (name))
				throw new ArgumentNullException (nameof (name));
			if (string.IsNullOrEmpty (systemImage))
				throw new ArgumentNullException (nameof (systemImage));

			// Check if AVD already exists — return it instead of failing
			if (!force) {
				var existing = (await ListAvdsAsync (cancellationToken).ConfigureAwait (false))
					.FirstOrDefault (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase));
				if (existing is not null)
					return existing;
			}

			// Detect orphaned AVD directory (folder exists without .ini registration).
			// Use --force to overwrite the orphaned directory.
			var avdDir = Path.Combine (
				Environment.GetFolderPath (Environment.SpecialFolder.UserProfile),
				".android", "avd", $"{name}.avd");
			if (Directory.Exists (avdDir))
				force = true;

			var args = $"create avd -n \"{name}\" -k \"{systemImage}\"";
			if (!string.IsNullOrEmpty (deviceProfile))
				args += $" -d \"{deviceProfile}\"";
			if (force)
				args += " --force";

			var stdout = new StringWriter ();
			var stderr = new StringWriter ();
			var psi = new ProcessStartInfo {
				FileName = AvdManagerPath!,
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true
			};
			ConfigureEnvironment (psi);

			// avdmanager prompts "Do you wish to create a custom hardware profile?" — answer "no"
			var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken,
				onStarted: p => {
					try {
						p.StandardInput.WriteLine ("no");
						p.StandardInput.Close ();
					} catch (System.IO.IOException) {
						// Process may have already exited
					}
				}).ConfigureAwait (false);

			if (exitCode != 0) {
				var errorOutput = stderr.ToString ().Trim ();
				if (string.IsNullOrEmpty (errorOutput))
					errorOutput = stdout.ToString ().Trim ();
				throw new InvalidOperationException ($"Failed to create AVD '{name}': {errorOutput}");
			}

			return new AvdInfo {
				Name = name,
				DeviceProfile = deviceProfile,
			};
		}

		public async Task DeleteAvdAsync (string name, CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				throw new InvalidOperationException ("AVD Manager not found.");

			var psi = new ProcessStartInfo {
				FileName = AvdManagerPath!,
				Arguments = $"delete avd --name \"{name}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			ConfigureEnvironment (psi);
			await ProcessUtils.StartProcess (psi, null, null, cancellationToken).ConfigureAwait (false);
		}

		internal static List<AvdInfo> ParseAvdListOutput (string output)
		{
			var avds = new List<AvdInfo> ();
			string? currentName = null, currentDevice = null, currentPath = null;

			foreach (var line in output.Split ('\n')) {
				var trimmed = line.Trim ();
				if (trimmed.StartsWith ("Name:", StringComparison.OrdinalIgnoreCase)) {
					if (currentName is not null)
						avds.Add (new AvdInfo { Name = currentName, DeviceProfile = currentDevice, Path = currentPath });
					currentName = trimmed.Substring (5).Trim ();
					currentDevice = currentPath = null;
				}
				else if (trimmed.StartsWith ("Device:", StringComparison.OrdinalIgnoreCase))
					currentDevice = trimmed.Substring (7).Trim ();
				else if (trimmed.StartsWith ("Path:", StringComparison.OrdinalIgnoreCase))
					currentPath = trimmed.Substring (5).Trim ();
			}

			if (currentName is not null)
				avds.Add (new AvdInfo { Name = currentName, DeviceProfile = currentDevice, Path = currentPath });

			return avds;
		}
	}
}

