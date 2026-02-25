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

		/// <summary>
		/// Creates a new <see cref="AvdManagerRunner"/>.
		/// </summary>
		/// <param name="getSdkPath">Function that returns the Android SDK path.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="getSdkPath"/> is null.</exception>
		public AvdManagerRunner (Func<string?> getSdkPath)
			: this (getSdkPath, null)
		{
		}

		/// <summary>
		/// Creates a new <see cref="AvdManagerRunner"/>.
		/// </summary>
		/// <param name="getSdkPath">Function that returns the Android SDK path.</param>
		/// <param name="getJdkPath">Optional function that returns the JDK path. When provided, sets JAVA_HOME for avdmanager processes.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="getSdkPath"/> is null.</exception>
		public AvdManagerRunner (Func<string?> getSdkPath, Func<string?>? getJdkPath)
		{
			this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
			this.getJdkPath = getJdkPath;
		}

		/// <summary>
		/// Gets the path to the avdmanager executable, or null if not found.
		/// </summary>
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

		/// <summary>
		/// Gets whether the AVD Manager is available.
		/// </summary>
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

		/// <summary>
		/// Lists all configured AVDs.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of configured AVDs.</returns>
		/// <exception cref="InvalidOperationException">Thrown when AVD Manager is not found.</exception>
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

		/// <summary>
		/// Creates a new AVD.
		/// </summary>
		/// <param name="name">The name for the new AVD.</param>
		/// <param name="systemImage">The system image package (e.g., "system-images;android-35;google_apis;x86_64").</param>
		/// <param name="deviceProfile">Optional device profile (e.g., "pixel_6"). Defaults to avdmanager's default.</param>
		/// <param name="force">When true, overwrites an existing AVD with the same name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Information about the created AVD.</returns>
		/// <exception cref="InvalidOperationException">Thrown when AVD Manager is not found or creation fails.</exception>
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

		/// <summary>
		/// Deletes an AVD.
		/// </summary>
		/// <param name="name">The name of the AVD to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <exception cref="InvalidOperationException">Thrown when AVD Manager is not found.</exception>
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

