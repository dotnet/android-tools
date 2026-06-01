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
	readonly string avdManagerPath;
	readonly IDictionary<string, string>? environmentVariables;
	readonly Action<TraceLevel, string> logger;

	/// <summary>
	/// Creates a new AvdManagerRunner with the full path to the avdmanager executable.
	/// </summary>
	/// <param name="avdManagerPath">Full path to avdmanager (e.g., "/path/to/sdk/cmdline-tools/latest/bin/avdmanager").</param>
	/// <param name="environmentVariables">Optional environment variables to pass to avdmanager processes.</param>
	/// <param name="logger">Optional logger callback for diagnostic messages.</param>
	public AvdManagerRunner (string avdManagerPath, IDictionary<string, string>? environmentVariables = null, Action<TraceLevel, string>? logger = null)
	{
		if (string.IsNullOrWhiteSpace (avdManagerPath))
			throw new ArgumentException ("Path to avdmanager must not be empty.", nameof (avdManagerPath));
		this.avdManagerPath = avdManagerPath;
		this.environmentVariables = environmentVariables;
		this.logger = logger ?? RunnerDefaults.NullLogger;
	}

	public async Task<IReadOnlyList<AvdInfo>> ListAvdsAsync (CancellationToken cancellationToken = default)
	{
		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "list", "avd");
		logger.Invoke (TraceLevel.Verbose, "Running: avdmanager list avd");
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, environmentVariables).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, "avdmanager list avd", stderr, stdout);

		return ParseAvdListOutput (stdout.ToString ());
	}

	/// <summary>
	/// Creates an AVD with the specified name and system image. If <paramref name="force"/> is <c>false</c>
	/// and an AVD with the same name already exists, returns the existing AVD without re-creating it.
	/// </summary>
	public async Task<AvdInfo> GetOrCreateAvdAsync (string name, string systemImage, string? deviceProfile = null,
		bool force = false, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace (name))
			throw new ArgumentException ("Value cannot be null or whitespace.", nameof (name));
		if (string.IsNullOrWhiteSpace (systemImage))
			throw new ArgumentException ("Value cannot be null or whitespace.", nameof (systemImage));

		// Check if AVD already exists — return it instead of failing
		if (!force) {
			var existing = (await ListAvdsAsync (cancellationToken).ConfigureAwait (false))
				.FirstOrDefault (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase));
			if (existing is not null) {
				logger.Invoke (TraceLevel.Verbose, $"AVD '{name}' already exists, returning existing");
				return existing;
			}
		}

		var args = new List<string> { "create", "avd", "-n", name, "-k", systemImage };
		if (deviceProfile is { Length: > 0 })
			args.AddRange (new [] { "-d", deviceProfile });
		if (force)
			args.Add ("--force");

		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, args.ToArray ());
		psi.RedirectStandardInput = true;

		// avdmanager prompts "Do you wish to create a custom hardware profile?" — answer "no"
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, environmentVariables,
			onStarted: p => {
				try {
					p.StandardInput.WriteLine ("no");
					p.StandardInput.Close ();
				} catch (IOException ex) {
					logger.Invoke (TraceLevel.Warning, $"Failed to write to avdmanager stdin: {ex.Message}");
				}
			}).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, $"avdmanager create avd -n {name}", stderr, stdout);

		// Re-list to get the actual path from avdmanager (respects ANDROID_USER_HOME/ANDROID_AVD_HOME)
		var avds = await ListAvdsAsync (cancellationToken).ConfigureAwait (false);
		var created = avds.FirstOrDefault (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase));
		if (created is not null)
			return created;

		throw new InvalidOperationException ($"avdmanager reported success but AVD '{name}' was not found in the list.");
	}

	public async Task DeleteAvdAsync (string name, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace (name))
			throw new ArgumentException ("Value cannot be null or whitespace.", nameof (name));

		// Idempotent: if the AVD doesn't exist, treat as success
		var avds = await ListAvdsAsync (cancellationToken).ConfigureAwait (false);
		if (!avds.Any (a => string.Equals (a.Name, name, StringComparison.OrdinalIgnoreCase))) {
			logger.Invoke (TraceLevel.Verbose, $"AVD '{name}' does not exist, nothing to delete");
			return;
		}

		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "delete", "avd", "--name", name);
		var exitCode = await ProcessUtils.StartProcess (psi, null, stderr, cancellationToken, environmentVariables).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, $"avdmanager delete avd --name {name}", stderr);
	}

	/// <summary>
	/// Lists available device profiles (hardware definitions) using <c>avdmanager list device --compact</c>.
	/// </summary>
	public async Task<IReadOnlyList<AvdDeviceProfile>> ListDeviceProfilesAsync (CancellationToken cancellationToken = default)
	{
		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (avdManagerPath, "list", "device", "--compact");
		logger.Invoke (TraceLevel.Verbose, "Running: avdmanager list device --compact");
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, environmentVariables).ConfigureAwait (false);

		ProcessUtils.ThrowIfFailed (exitCode, "avdmanager list device --compact", stderr, stdout);

		return ParseCompactDeviceListOutput (stdout.ToString ());
	}

	internal static IReadOnlyList<AvdDeviceProfile> ParseCompactDeviceListOutput (string output)
	{
		var profiles = new List<AvdDeviceProfile> ();

		foreach (var line in output.Split ('\n')) {
			var trimmed = line.Trim ();
			if (trimmed.Length > 0)
				profiles.Add (new AvdDeviceProfile (trimmed));
		}

		return profiles;
	}

	/// <summary>
	/// Lists available AVD skins by scanning the SDK for built-in and downloaded skin definitions.
	/// </summary>
	/// <remarks>
	/// The following SDK locations are scanned and the discovered skin directory names are deduplicated:
	/// <list type="bullet">
	///   <item><description><c>&lt;sdk&gt;/skins/</c> — top-level shared skins.</description></item>
	///   <item><description><c>&lt;sdk&gt;/platforms/&lt;api&gt;/skins/</c> — per-platform built-in skins (e.g. <c>HVGA</c>, <c>WVGA800</c>, <c>WXGA720</c>).</description></item>
	///   <item><description><c>&lt;sdk&gt;/add-ons/&lt;addon&gt;/skins/</c> — legacy SDK add-on skins (e.g. older Google APIs add-ons).</description></item>
	///   <item><description><c>&lt;sdk&gt;/system-images/&lt;api&gt;/&lt;tag&gt;/&lt;abi&gt;/skins/</c> — per-system-image skins shipped with recent Pixel images.</description></item>
	/// </list>
	/// </remarks>
	/// <param name="sdkPath">Root path of the Android SDK.</param>
	/// <param name="cancellationToken">Cancellation token observed throughout directory enumeration.</param>
	/// <returns>A sorted, deduplicated list of skin directory names discovered across the SDK.</returns>
	public Task<IReadOnlyList<string>> ListAvdSkinsAsync (string sdkPath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace (sdkPath))
			throw new ArgumentException ("SDK path must not be empty.", nameof (sdkPath));

		// Skin enumeration is purely synchronous filesystem work, but it can walk a large SDK tree;
		// offload to the thread pool so callers don't block, and to keep the runner's async surface consistent.
		return Task.Run (() => EnumerateSkins (sdkPath, cancellationToken), cancellationToken);
	}

	internal static IReadOnlyList<string> EnumerateSkins (string sdkPath, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested ();

		// Skin directory names round-trip case-sensitively on Linux/macOS, so only collapse case on Windows.
		var skins = new SortedSet<string> (OS.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		// Standalone skins: <sdk>/skins/<skinName>/
		AddSkinDirectories (skins, Path.Combine (sdkPath, "skins"), cancellationToken);

		// Per-platform built-in skins: <sdk>/platforms/<api>/skins/<skinName>/
		// This is where the SDK Platforms package ships skins (HVGA, WVGA800, WXGA720, ...).
		AddNestedSkinDirectories (skins, Path.Combine (sdkPath, "platforms"), cancellationToken);

		// Legacy add-on skins: <sdk>/add-ons/<addon>/skins/<skinName>/
		AddNestedSkinDirectories (skins, Path.Combine (sdkPath, "add-ons"), cancellationToken);

		// Per-system-image skins: <sdk>/system-images/<api>/<tag>/<abi>/skins/<skinName>/
		var systemImagesDir = Path.Combine (sdkPath, "system-images");
		if (Directory.Exists (systemImagesDir)) {
			try {
				foreach (var apiDir in Directory.EnumerateDirectories (systemImagesDir)) {
					cancellationToken.ThrowIfCancellationRequested ();
					try {
						foreach (var tagDir in Directory.EnumerateDirectories (apiDir)) {
							cancellationToken.ThrowIfCancellationRequested ();
							foreach (var abiDir in Directory.EnumerateDirectories (tagDir)) {
								cancellationToken.ThrowIfCancellationRequested ();
								AddSkinDirectories (skins, Path.Combine (abiDir, "skins"), cancellationToken);
							}
						}
					} catch (IOException) {
					} catch (UnauthorizedAccessException) {
					}
				}
			} catch (IOException) {
			} catch (UnauthorizedAccessException) {
			}
		}

		return skins.ToList ();
	}

	// Walk a single level of subdirectories (e.g. platforms/<api> or add-ons/<addon>) and
	// pull skin names out of each child's "skins" subdirectory if it exists.
	static void AddNestedSkinDirectories (SortedSet<string> skins, string parentDir, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		if (!Directory.Exists (parentDir))
			return;
		try {
			foreach (var childDir in Directory.EnumerateDirectories (parentDir)) {
				cancellationToken.ThrowIfCancellationRequested ();
				AddSkinDirectories (skins, Path.Combine (childDir, "skins"), cancellationToken);
			}
		} catch (IOException) {
		} catch (UnauthorizedAccessException) {
		}
	}

	static void AddSkinDirectories (SortedSet<string> skins, string directory, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		if (!Directory.Exists (directory))
			return;
		try {
			foreach (var skinDir in Directory.EnumerateDirectories (directory)) {
				cancellationToken.ThrowIfCancellationRequested ();
				skins.Add (Path.GetFileName (skinDir));
			}
		} catch (IOException) {
		} catch (UnauthorizedAccessException) {
		}
	}

	internal static IReadOnlyList<AvdInfo> ParseAvdListOutput (string output)
	{
		var avds = new List<AvdInfo> ();
		string? currentName = null, currentDevice = null, currentPath = null;

		foreach (var line in output.Split ('\n')) {
			var trimmed = line.Trim ();
			if (trimmed.StartsWith ("Name:", StringComparison.OrdinalIgnoreCase)) {
				if (currentName is not null)
					avds.Add (new AvdInfo (currentName, currentDevice, currentPath));
				currentName = trimmed.Substring (5).Trim ();
				currentDevice = currentPath = null;
			}
			else if (trimmed.StartsWith ("Device:", StringComparison.OrdinalIgnoreCase))
				currentDevice = trimmed.Substring (7).Trim ();
			else if (trimmed.StartsWith ("Path:", StringComparison.OrdinalIgnoreCase))
				currentPath = trimmed.Substring (5).Trim ();
		}

		if (currentName is not null)
			avds.Add (new AvdInfo (currentName, currentDevice, currentPath));

		return avds;
	}

}

