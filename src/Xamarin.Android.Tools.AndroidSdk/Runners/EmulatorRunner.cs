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
	readonly string emulatorPath;
	readonly IDictionary<string, string>? environmentVariables;
	readonly Action<TraceLevel, string>? logger;

	/// <summary>
	/// Creates a new EmulatorRunner with the full path to the emulator executable.
	/// </summary>
	/// <param name="emulatorPath">Full path to the emulator executable (e.g., "/path/to/sdk/emulator/emulator").</param>
	/// <param name="environmentVariables">Optional environment variables to pass to emulator processes.</param>
	/// <param name="logger">Optional logger callback for diagnostic messages.</param>
	public EmulatorRunner (string emulatorPath, IDictionary<string, string>? environmentVariables = null, Action<TraceLevel, string>? logger = null)
	{
		if (string.IsNullOrWhiteSpace (emulatorPath))
			throw new ArgumentException ("Path to emulator must not be empty.", nameof (emulatorPath));
		this.emulatorPath = emulatorPath;
		this.environmentVariables = environmentVariables;
		this.logger = logger;
	}

	public Process StartAvd (string avdName, bool coldBoot = false, IEnumerable<string>? additionalArgs = null)
	{
		var args = new List<string> { "-avd", avdName };
		if (coldBoot)
			args.Add ("-no-snapshot-load");
		if (additionalArgs != null)
			args.AddRange (additionalArgs);

		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, args.ToArray ());

		// Redirect stdout/stderr so the emulator process doesn't inherit the
		// caller's pipes. Without this, parent processes (e.g. VS Code spawn)
		// never see the 'close' event because the emulator holds the pipes open.
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		logger?.Invoke (TraceLevel.Verbose, $"Starting emulator AVD '{avdName}'");

		var process = new Process { StartInfo = psi };

		// Forward emulator output to the logger so crash messages (e.g. "HAX is
		// not working", "image not found") are captured instead of silently lost.
		process.OutputDataReceived += (_, e) => {
			if (e.Data != null)
				logger?.Invoke (TraceLevel.Verbose, $"[emulator] {e.Data}");
		};
		process.ErrorDataReceived += (_, e) => {
			if (e.Data != null)
				logger?.Invoke (TraceLevel.Warning, $"[emulator] {e.Data}");
		};

		process.Start ();

		// Drain redirected streams asynchronously to prevent pipe buffer deadlocks
		process.BeginOutputReadLine ();
		process.BeginErrorReadLine ();

		return process;
	}

	public async Task<IReadOnlyList<string>> ListAvdNamesAsync (CancellationToken cancellationToken = default)
	{
		using var stdout = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, "-list-avds");

		logger?.Invoke (TraceLevel.Verbose, "Running: emulator -list-avds");
		await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken, environmentVariables).ConfigureAwait (false);

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
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace (deviceOrAvdName))
			throw new ArgumentException ("Device or AVD name must not be empty.", nameof (deviceOrAvdName));
		if (adbRunner == null)
			throw new ArgumentNullException (nameof (adbRunner));

		options ??= new EmulatorBootOptions ();
		if (options.BootTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (options), "BootTimeout must be positive.");
		if (options.PollInterval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (options), "PollInterval must be positive.");

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
			return await WaitForFullBootAsync (adbRunner, runningSerial, options, cancellationToken).ConfigureAwait (false);
		}

		// Phase 3: Launch the emulator
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

		// Poll for the new emulator serial to appear.
		// If the boot times out or is cancelled, terminate the process we spawned
		// to avoid leaving orphan emulator processes.
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
			return await WaitForFullBootAsync (adbRunner, newSerial, options, timeoutCts.Token).ConfigureAwait (false);
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			TryKillProcess (emulatorProcess);
			return new EmulatorBootResult {
				Success = false,
				ErrorMessage = $"Timed out waiting for emulator '{deviceOrAvdName}' to boot within {options.BootTimeout.TotalSeconds}s.",
			};
		} catch {
			TryKillProcess (emulatorProcess);
			throw;
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

	static void TryKillProcess (Process process)
	{
		try {
			if (!process.HasExited)
				process.Kill ();
		} catch {
			// Best-effort: process may have already exited between check and kill
		} finally {
			process.Dispose ();
		}
	}

	async Task<EmulatorBootResult> WaitForFullBootAsync (
		AdbRunner adbRunner,
		string serial,
		EmulatorBootOptions options,
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

