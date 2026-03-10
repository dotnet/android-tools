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

			var emulatorDir = Path.Combine (sdkPath, "emulator");

			if (OS.IsWindows) {
				// Prefer .exe, fall back to .bat/.cmd (older SDK versions)
				foreach (var ext in new [] { ".exe", ".bat", ".cmd" }) {
					var candidate = Path.Combine (emulatorDir, "emulator" + ext);
					if (File.Exists (candidate))
						return candidate;
				}
				return null;
			}

			var path = Path.Combine (emulatorDir, "emulator");
			return File.Exists (path) ? path : null;
		}
	}

	public bool IsAvailable => EmulatorPath is not null;

	string RequireEmulatorPath ()
	{
		return EmulatorPath ?? throw new InvalidOperationException ("Android Emulator not found.");
	}

	void ConfigureEnvironment (ProcessStartInfo psi)
	{
		AndroidEnvironmentHelper.ConfigureEnvironment (psi, getSdkPath (), getJdkPath?.Invoke ());
	}

	public Process StartAvd (string avdName, bool coldBoot = false, string? additionalArgs = null)
	{
		var emulatorPath = RequireEmulatorPath ();

		var args = new List<string> { "-avd", avdName };
		if (coldBoot)
			args.Add ("-no-snapshot-load");
		if (!string.IsNullOrEmpty (additionalArgs))
			args.Add (additionalArgs);

		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, args.ToArray ());
		ConfigureEnvironment (psi);

		// Redirect stdout/stderr so the emulator process doesn't inherit the
		// caller's pipes. Without this, parent processes (e.g. VS Code spawn)
		// never see the 'close' event because the emulator holds the pipes open.
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		var process = new Process { StartInfo = psi };
		process.Start ();

		return process;
	}

	public async Task<IReadOnlyList<string>> ListAvdNamesAsync (CancellationToken cancellationToken = default)
	{
		var emulatorPath = RequireEmulatorPath ();

		using var stdout = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, "-list-avds");
		ConfigureEnvironment (psi);

		await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

		return ParseListAvdsOutput (stdout.ToString ());
	}

	internal static List<string> ParseListAvdsOutput (string output)
	{
		var avds = new List<string> ();
		foreach (var line in output.Split ('\n')) {
			var trimmed = line.Trim ();
			if (!string.IsNullOrEmpty (trimmed))
				avds.Add (trimmed);
		}
		return avds;
	}

	/// <summary>
	/// Boots an emulator and waits for it to be fully booted.
	/// Ported from dotnet/android BootAndroidEmulator MSBuild task.
	/// </summary>
	public async Task<EmulatorBootResult> BootAndWaitAsync (
		string deviceOrAvdName,
		AdbRunner adbRunner,
		EmulatorBootOptions? options = null,
		Action<TraceLevel, string>? logger = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace (deviceOrAvdName))
			throw new ArgumentException ("Device or AVD name must not be empty.", nameof (deviceOrAvdName));
		if (adbRunner == null)
			throw new ArgumentNullException (nameof (adbRunner));

		options = options ?? new EmulatorBootOptions ();
		void Log (TraceLevel level, string message) => logger?.Invoke (level, message);

		Log (TraceLevel.Info, $"Booting emulator for '{deviceOrAvdName}'...");

		// Phase 1: Check if deviceOrAvdName is already an online ADB device by serial
		var devices = await adbRunner.ListDevicesAsync (cancellationToken).ConfigureAwait (false);
		var onlineDevice = devices.FirstOrDefault (d =>
			d.Status == AdbDeviceStatus.Online &&
			string.Equals (d.Serial, deviceOrAvdName, StringComparison.OrdinalIgnoreCase));

		if (onlineDevice != null) {
			Log (TraceLevel.Info, $"Device '{deviceOrAvdName}' is already online.");
			return new EmulatorBootResult { Success = true, Serial = onlineDevice.Serial };
		}

		// Phase 2: Check if AVD is already running (possibly still booting)
		var runningSerial = FindRunningAvdSerial (devices, deviceOrAvdName);
		if (runningSerial != null) {
			Log (TraceLevel.Info, $"AVD '{deviceOrAvdName}' is already running as '{runningSerial}', waiting for full boot...");
			return await WaitForFullBootAsync (adbRunner, runningSerial, options, logger, cancellationToken).ConfigureAwait (false);
		}

		// Phase 3: Launch the emulator
		if (EmulatorPath == null) {
			return new EmulatorBootResult {
				Success = false,
				ErrorMessage = "Android Emulator not found. Ensure the Android SDK is installed and the emulator is available.",
			};
		}

		Log (TraceLevel.Info, $"Launching AVD '{deviceOrAvdName}'...");
		Process emulatorProcess;
		try {
			emulatorProcess = StartAvd (deviceOrAvdName, options.ColdBoot, options.AdditionalArgs);
		} catch (Exception ex) {
			return new EmulatorBootResult {
				Success = false,
				ErrorMessage = $"Failed to launch emulator: {ex.Message}",
			};
		}

		// Poll for the new emulator serial to appear
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeoutCts.CancelAfter (options.BootTimeout);

		try {
			string? newSerial = null;
			while (newSerial == null) {
				timeoutCts.Token.ThrowIfCancellationRequested ();
				await Task.Delay (options.PollInterval, timeoutCts.Token).ConfigureAwait (false);

				devices = await adbRunner.ListDevicesAsync (timeoutCts.Token).ConfigureAwait (false);
				newSerial = FindRunningAvdSerial (devices, deviceOrAvdName);
			}

			Log (TraceLevel.Info, $"Emulator appeared as '{newSerial}', waiting for full boot...");
			return await WaitForFullBootAsync (adbRunner, newSerial, options, logger, timeoutCts.Token).ConfigureAwait (false);
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			return new EmulatorBootResult {
				Success = false,
				ErrorMessage = $"Timed out waiting for emulator '{deviceOrAvdName}' to boot within {options.BootTimeout.TotalSeconds}s.",
			};
		}
	}

	static string? FindRunningAvdSerial (IReadOnlyList<AdbDeviceInfo> devices, string avdName)
	{
		foreach (var d in devices) {
			if (d.Type == AdbDeviceType.Emulator &&
				!string.IsNullOrEmpty (d.AvdName) &&
				string.Equals (d.AvdName, avdName, StringComparison.OrdinalIgnoreCase)) {
				return d.Serial;
			}
		}
		return null;
	}

	async Task<EmulatorBootResult> WaitForFullBootAsync (
		AdbRunner adbRunner,
		string serial,
		EmulatorBootOptions options,
		Action<TraceLevel, string>? logger,
		CancellationToken cancellationToken)
	{
		void Log (TraceLevel level, string message) => logger?.Invoke (level, message);

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeoutCts.CancelAfter (options.BootTimeout);

		try {
			while (true) {
				timeoutCts.Token.ThrowIfCancellationRequested ();

				var bootCompleted = await adbRunner.GetShellPropertyAsync (serial, "sys.boot_completed", timeoutCts.Token).ConfigureAwait (false);
				if (string.Equals (bootCompleted, "1", StringComparison.Ordinal)) {
					var pmResult = await adbRunner.RunShellCommandAsync (serial, "pm path android", timeoutCts.Token).ConfigureAwait (false);
					if (pmResult != null && pmResult.StartsWith ("package:", StringComparison.Ordinal)) {
						Log (TraceLevel.Info, $"Emulator '{serial}' is fully booted.");
						return new EmulatorBootResult { Success = true, Serial = serial };
					}
				}

				await Task.Delay (options.PollInterval, timeoutCts.Token).ConfigureAwait (false);
			}
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			return new EmulatorBootResult {
				Success = false,
				Serial = serial,
				ErrorMessage = $"Timed out waiting for emulator '{serial}' to fully boot within {options.BootTimeout.TotalSeconds}s.",
			};
		}
	}
}

