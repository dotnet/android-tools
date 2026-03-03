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

			var ext = OS.IsWindows ? ".bat" : "";
			var cmdlineToolsDir = Path.Combine (sdkPath, "cmdline-tools");

			if (Directory.Exists (cmdlineToolsDir)) {
				// Versioned dirs sorted descending, then "latest" as fallback
				var searchDirs = Directory.GetDirectories (cmdlineToolsDir)
					.Select (Path.GetFileName)
					.Where (n => n != "latest" && !string.IsNullOrEmpty (n))
					.OrderByDescending (n => Version.TryParse (n, out var v) ? v : new Version (0, 0))
					.Append ("latest");

				foreach (var dir in searchDirs) {
					var toolPath = Path.Combine (cmdlineToolsDir, dir!, "bin", "avdmanager" + ext);
					if (File.Exists (toolPath))
						return toolPath;
				}
			}

			// Legacy fallback: tools/bin/avdmanager
			var legacyPath = Path.Combine (sdkPath, "tools", "bin", "avdmanager" + ext);
			return File.Exists (legacyPath) ? legacyPath : null;
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

		if (exitCode != 0)
			throw new InvalidOperationException ($"avdmanager list avd failed (exit code {exitCode}): {stderr.ToString ().Trim ()}");

		return ParseAvdListOutput (stdout.ToString ());
	}

	public async Task<AvdInfo> CreateAvdAsync (string name, string systemImage, string? deviceProfile = null,
		bool force = false, CancellationToken cancellationToken = default)
	{
		var avdManagerPath = RequireAvdManagerPath ();
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
		var avdDir = Path.Combine (
			Environment.GetFolderPath (Environment.SpecialFolder.UserProfile),
			".android", "avd", $"{name}.avd");
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

		if (exitCode != 0) {
			var errorOutput = stderr.ToString ().Trim ();
			if (string.IsNullOrEmpty (errorOutput))
				errorOutput = stdout.ToString ().Trim ();
			throw new InvalidOperationException ($"Failed to create AVD '{name}': {errorOutput}");
		}

		return new AvdInfo {
			Name = name,
			DeviceProfile = deviceProfile,
			Path = avdDir,
		};
	}

	public async Task DeleteAvdAsync (string name, CancellationToken cancellationToken = default)
	{
		var avdManagerPath = RequireAvdManagerPath ();

		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "delete", "avd", "--name", name);
		ConfigureEnvironment (psi);
		var exitCode = await ProcessUtils.StartProcess (psi, null, stderr, cancellationToken).ConfigureAwait (false);

		if (exitCode != 0)
			throw new InvalidOperationException ($"Failed to delete AVD '{name}': {stderr.ToString ().Trim ()}");
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

