// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

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

			return ProcessUtils.FindCmdlineTool (sdkPath, "avdmanager", OS.IsWindows ? ".bat" : "");
		}
	}

	public bool IsAvailable => !string.IsNullOrEmpty (AvdManagerPath);

	string RequireAvdManagerPath ()
	{
		return AvdManagerPath ?? throw new InvalidOperationException ("AVD Manager not found.");
	}

	void ConfigureEnvironment (ProcessStartInfo psi)
	{
		AndroidEnvironmentHelper.ConfigureEnvironment (psi, getSdkPath (), getJdkPath?.Invoke ());
	}

	public async Task<IReadOnlyList<AvdInfo>> ListAvdsAsync (CancellationToken cancellationToken = default)
	{
		var avdManagerPath = RequireAvdManagerPath ();

		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "list", "avd");
		ConfigureEnvironment (psi);
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, "avdmanager list avd", stderr.ToString ());

		return ParseAvdListOutput (stdout.ToString ());
	}

	public async Task<AvdInfo> CreateAvdAsync (string name, string systemImage, string? deviceProfile = null,
		bool force = false, CancellationToken cancellationToken = default)
	{
		ProcessUtils.ValidateNotNullOrEmpty (name, nameof (name));
		ProcessUtils.ValidateNotNullOrEmpty (systemImage, nameof (systemImage));

		var avdManagerPath = RequireAvdManagerPath ();

		// Check if AVD already exists — return it instead of failing
		if (!force) {
			var existing = (await ListAvdsAsync (cancellationToken).ConfigureAwait (false))
				.FirstOrDefault (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase));
			if (existing is not null)
				return existing;
		}

		// Detect orphaned AVD directory (folder exists without .ini registration).
		var avdDir = Path.Combine (GetAvdRootDirectory (), $"{name}.avd");
		if (Directory.Exists (avdDir))
			force = true;

		var args = new List<string> { "create", "avd", "-n", name, "-k", systemImage };
		if (!string.IsNullOrEmpty (deviceProfile))
			args.AddRange (new [] { "-d", deviceProfile });
		if (force)
			args.Add ("--force");

		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, args.ToArray ());
		psi.RedirectStandardInput = true;
		ConfigureEnvironment (psi);

		// avdmanager prompts "Do you wish to create a custom hardware profile?" — answer "no"
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken,
			onStarted: p => {
				try {
					p.StandardInput.WriteLine ("no");
					p.StandardInput.Close ();
				} catch (IOException) {
					// Process may have already exited
				}
			}).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, $"avdmanager create avd -n {name}", stderr.ToString (), stdout.ToString ());

		// Re-list to get the actual path from avdmanager (respects ANDROID_USER_HOME/ANDROID_AVD_HOME)
		var avds = await ListAvdsAsync (cancellationToken).ConfigureAwait (false);
		var created = avds.FirstOrDefault (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase));
		if (created is not null)
			return created;

		// Fallback if re-list didn't find it
		return new AvdInfo {
			Name = name,
			DeviceProfile = deviceProfile,
			Path = Path.Combine (GetAvdRootDirectory (), $"{name}.avd"),
		};
	}

	public async Task DeleteAvdAsync (string name, CancellationToken cancellationToken = default)
	{
		ProcessUtils.ValidateNotNullOrEmpty (name, nameof (name));

		var avdManagerPath = RequireAvdManagerPath ();

		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "delete", "avd", "--name", name);
		ConfigureEnvironment (psi);
		var exitCode = await ProcessUtils.StartProcess (psi, null, stderr, cancellationToken).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, $"avdmanager delete avd --name {name}", stderr.ToString ());
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

	/// <summary>
	/// Resolves the AVD root directory, respecting ANDROID_AVD_HOME and ANDROID_USER_HOME.
	/// </summary>
	static string GetAvdRootDirectory ()
	{
		// ANDROID_AVD_HOME takes highest priority
		var avdHome = Environment.GetEnvironmentVariable ("ANDROID_AVD_HOME");
		if (!string.IsNullOrEmpty (avdHome))
			return avdHome;

		// ANDROID_USER_HOME/avd is the next option
		var userHome = Environment.GetEnvironmentVariable (EnvironmentVariableNames.AndroidUserHome);
		if (!string.IsNullOrEmpty (userHome))
			return Path.Combine (userHome, "avd");

		// Default: ~/.android/avd
		return Path.Combine (
			Environment.GetFolderPath (Environment.SpecialFolder.UserProfile),
			".android", "avd");
	}
}

