// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

/// <summary>
/// Runs Android Debug Bridge (adb) commands.
/// Parsing logic ported from dotnet/android GetAvailableAndroidDevices task.
/// </summary>
public class AdbRunner
{
	readonly Func<string?> getSdkPath;

	// Pattern to match device lines: <serial> <state> [key:value ...]
	// Ported from dotnet/android GetAvailableAndroidDevices.AdbDevicesRegex
	static readonly Regex AdbDevicesRegex = new Regex (
		@"^([^\s]+)\s+(device|offline|unauthorized|no permissions)\s*(.*)$", RegexOptions.Compiled);
	static readonly Regex ApiRegex = new Regex (@"\bApi\b", RegexOptions.Compiled);

	public AdbRunner (Func<string?> getSdkPath)
	{
		this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
	}

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

	public bool IsAvailable => AdbPath is not null;

	string RequireAdb ()
	{
		return AdbPath ?? throw new InvalidOperationException ("ADB not found.");
	}

	ProcessStartInfo CreateAdbProcess (string adbPath, params string [] args)
	{
		var psi = ProcessUtils.CreateProcessStartInfo (adbPath, args);
		AndroidEnvironmentHelper.ConfigureEnvironment (psi, getSdkPath (), null);
		return psi;
	}

	/// <summary>
	/// Lists connected devices using 'adb devices -l'.
	/// For emulators, queries the AVD name using 'adb -s &lt;serial&gt; emu avd name'.
	/// </summary>
	public virtual async Task<IReadOnlyList<AdbDeviceInfo>> ListDevicesAsync (CancellationToken cancellationToken = default)
	{
		var adb = RequireAdb ();
		using var stdout = new StringWriter ();
		var psi = CreateAdbProcess (adb, "devices", "-l");
		await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

		var devices = ParseAdbDevicesOutput (stdout.ToString ());

		// For each emulator, try to get the AVD name
		foreach (var device in devices) {
			if (device.Type == AdbDeviceType.Emulator) {
				device.AvdName = await GetEmulatorAvdNameAsync (adb, device.Serial, cancellationToken).ConfigureAwait (false);
				device.Description = BuildDeviceDescription (device);
			}
		}

		return devices;
	}

	/// <summary>
	/// Queries the emulator for its AVD name using 'adb -s &lt;serial&gt; emu avd name'.
	/// Ported from dotnet/android GetAvailableAndroidDevices.GetEmulatorAvdName.
	/// </summary>
	public async Task<string?> GetEmulatorAvdNameAsync (string adbPath, string serial, CancellationToken cancellationToken = default)
	{
		try {
			using var stdout = new StringWriter ();
			var psi = CreateAdbProcess (adbPath, "-s", serial, "emu", "avd", "name");
			await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

			foreach (var line in stdout.ToString ().Split ('\n')) {
				var trimmed = line.Trim ();
				if (!string.IsNullOrEmpty (trimmed) &&
					!string.Equals (trimmed, "OK", StringComparison.OrdinalIgnoreCase)) {
					return trimmed;
				}
			}
		} catch (OperationCanceledException) {
			throw;
		} catch {
			// Silently ignore failures (emulator may not support this command)
		}

		return null;
	}

	public async Task WaitForDeviceAsync (string? serial = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
	{
		var adb = RequireAdb ();
		var effectiveTimeout = timeout ?? TimeSpan.FromSeconds (60);

		var args = string.IsNullOrEmpty (serial)
			? new [] { "wait-for-device" }
			: new [] { "-s", serial!, "wait-for-device" };

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

	/// <summary>
	/// Runs 'adb -s {serial} shell getprop {propertyName}' and returns the first non-empty line, or null.
	/// </summary>
	public virtual async Task<string?> GetShellPropertyAsync (string serial, string propertyName, CancellationToken cancellationToken = default)
	{
		var adb = RequireAdb ();
		using var stdout = new StringWriter ();
		var psi = CreateAdbProcess (adb, "-s", serial, "shell", "getprop", propertyName);
		await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

		return FirstNonEmptyLine (stdout.ToString ());
	}

	/// <summary>
	/// Runs 'adb -s {serial} shell {command}' and returns the first non-empty line, or null.
	/// </summary>
	public virtual async Task<string?> RunShellCommandAsync (string serial, string command, CancellationToken cancellationToken = default)
	{
		var adb = RequireAdb ();
		using var stdout = new StringWriter ();
		var psi = CreateAdbProcess (adb, "-s", serial, "shell", command);
		await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

		return FirstNonEmptyLine (stdout.ToString ());
	}

	static string? FirstNonEmptyLine (string output)
	{
		foreach (var line in output.Split ('\n')) {
			var trimmed = line.Trim ();
			if (!string.IsNullOrEmpty (trimmed))
				return trimmed;
		}
		return null;
	}

	/// <summary>
	/// Parses the output of 'adb devices -l'.
	/// Ported from dotnet/android GetAvailableAndroidDevices.ParseAdbDevicesOutput.
	/// </summary>
	public static List<AdbDeviceInfo> ParseAdbDevicesOutput (string output)
	{
		var devices = new List<AdbDeviceInfo> ();

		foreach (var line in output.Split ('\n')) {
			var trimmed = line.Trim ();
			if (string.IsNullOrEmpty (trimmed) || trimmed.IndexOf ("List of devices", StringComparison.OrdinalIgnoreCase) >= 0)
				continue;

			var match = AdbDevicesRegex.Match (trimmed);
			if (!match.Success)
				continue;

			var serial = match.Groups [1].Value.Trim ();
			var state = match.Groups [2].Value.Trim ();
			var properties = match.Groups [3].Value.Trim ();

			// Parse key:value pairs from the properties string
			var propDict = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrEmpty (properties)) {
				var pairs = properties.Split (new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var pair in pairs) {
					var colonIndex = pair.IndexOf (':');
					if (colonIndex > 0 && colonIndex < pair.Length - 1) {
						var key = pair.Substring (0, colonIndex);
						var value = pair.Substring (colonIndex + 1);
						propDict [key] = value;
					}
				}
			}

			var deviceType = serial.StartsWith ("emulator-", StringComparison.OrdinalIgnoreCase)
				? AdbDeviceType.Emulator
				: AdbDeviceType.Device;

			var device = new AdbDeviceInfo {
				Serial = serial,
				Type = deviceType,
				Status = MapAdbStateToStatus (state),
			};

			if (propDict.TryGetValue ("model", out var model))
				device.Model = model;
			if (propDict.TryGetValue ("product", out var product))
				device.Product = product;
			if (propDict.TryGetValue ("device", out var deviceCodeName))
				device.Device = deviceCodeName;
			if (propDict.TryGetValue ("transport_id", out var transportId))
				device.TransportId = transportId;

			// Build description (will be updated later if emulator AVD name is available)
			device.Description = BuildDeviceDescription (device);

			devices.Add (device);
		}

		return devices;
	}

	/// <summary>
	/// Maps adb device states to status values.
	/// Ported from dotnet/android GetAvailableAndroidDevices.MapAdbStateToStatus.
	/// </summary>
	public static AdbDeviceStatus MapAdbStateToStatus (string adbState)
	{
		switch (adbState.ToLowerInvariant ()) {
		case "device": return AdbDeviceStatus.Online;
		case "offline": return AdbDeviceStatus.Offline;
		case "unauthorized": return AdbDeviceStatus.Unauthorized;
		case "no permissions": return AdbDeviceStatus.NoPermissions;
		default: return AdbDeviceStatus.Unknown;
		}
	}

	/// <summary>
	/// Builds a human-friendly description for a device.
	/// Priority: AVD name (for emulators) > model > product > device > serial.
	/// Ported from dotnet/android GetAvailableAndroidDevices.BuildDeviceDescription.
	/// </summary>
	public static string BuildDeviceDescription (AdbDeviceInfo device)
	{
		if (device.Type == AdbDeviceType.Emulator && !string.IsNullOrEmpty (device.AvdName))
			return FormatDisplayName (device.AvdName!);

		if (!string.IsNullOrEmpty (device.Model))
			return device.Model!.Replace ('_', ' ');

		if (!string.IsNullOrEmpty (device.Product))
			return device.Product!.Replace ('_', ' ');

		if (!string.IsNullOrEmpty (device.Device))
			return device.Device!.Replace ('_', ' ');

		return device.Serial;
	}

	/// <summary>
	/// Formats an AVD name into a user-friendly display name.
	/// Replaces underscores with spaces, applies title case, and capitalizes "API".
	/// Ported from dotnet/android GetAvailableAndroidDevices.FormatDisplayName.
	/// </summary>
	public static string FormatDisplayName (string avdName)
	{
		if (string.IsNullOrEmpty (avdName))
			return avdName ?? string.Empty;

		var textInfo = CultureInfo.InvariantCulture.TextInfo;
		avdName = textInfo.ToTitleCase (avdName.Replace ('_', ' '));

		// Replace "Api" with "API"
		avdName = ApiRegex.Replace (avdName, "API");
		return avdName;
	}

	/// <summary>
	/// Merges devices from adb with available emulators from 'emulator -list-avds'.
	/// Running emulators are not duplicated. Non-running emulators are added with Status=NotRunning.
	/// Ported from dotnet/android GetAvailableAndroidDevices.MergeDevicesAndEmulators.
	/// </summary>
	public static List<AdbDeviceInfo> MergeDevicesAndEmulators (IReadOnlyList<AdbDeviceInfo> adbDevices, IReadOnlyList<string> availableEmulators)
	{
		var result = new List<AdbDeviceInfo> (adbDevices);

		// Build a set of AVD names that are already running
		var runningAvdNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		foreach (var device in adbDevices) {
			if (!string.IsNullOrEmpty (device.AvdName))
				runningAvdNames.Add (device.AvdName!);
		}

		// Add non-running emulators
		foreach (var avdName in availableEmulators) {
			if (runningAvdNames.Contains (avdName))
				continue;

			var displayName = FormatDisplayName (avdName);
			result.Add (new AdbDeviceInfo {
				Serial = avdName,
				Description = displayName + " (Not Running)",
				Type = AdbDeviceType.Emulator,
				Status = AdbDeviceStatus.NotRunning,
				AvdName = avdName,
			});
		}

		// Sort: online devices first, then not-running emulators, alphabetically by description
		result.Sort ((a, b) => {
			var aNotRunning = a.Status == AdbDeviceStatus.NotRunning;
			var bNotRunning = b.Status == AdbDeviceStatus.NotRunning;

			if (aNotRunning != bNotRunning)
				return aNotRunning ? 1 : -1;

			return string.Compare (a.Description, b.Description, StringComparison.OrdinalIgnoreCase);
		});

		return result;
	}
}

