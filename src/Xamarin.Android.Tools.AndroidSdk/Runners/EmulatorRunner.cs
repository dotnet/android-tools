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
	readonly Action<TraceLevel, string> logger;

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
		this.logger = logger ?? RunnerDefaults.NullLogger;
	}

	/// <summary>
	/// Launches an emulator process for the specified AVD and returns immediately with enriched
	/// launch information (process, PID, ports, serial, log path).
	/// The caller is responsible for managing the process lifetime (e.g., killing it on shutdown).
	/// This method does <b>not</b> wait for the emulator to finish booting.
	/// To launch <i>and</i> wait until the device is fully booted, use <see cref="BootEmulatorAsync"/> instead.
	/// </summary>
	/// <param name="avdName">Name of the AVD to launch (as shown by <c>emulator -list-avds</c>).</param>
	/// <param name="coldBoot">When <c>true</c>, forces a cold boot by passing <c>-no-snapshot-load</c>.</param>
	/// <param name="consolePort">
	/// Optional console port to pre-assign via <c>-ports</c> (must be an even number in 5554–5682).
	/// When specified the serial is known immediately; otherwise it is resolved by parsing stdout/stderr.
	/// </param>
	/// <param name="adbPort">
	/// Optional ADB port to pair with <paramref name="consolePort"/>. Defaults to
	/// <c>consolePort + 1</c> when <paramref name="consolePort"/> is provided.
	/// </param>
	/// <param name="logFile">
	/// Optional path for the emulator log file, passed via <c>-logfile</c>. When <c>null</c> the
	/// default AOSP path is resolved from <c>ANDROID_AVD_HOME</c> / <c>ANDROID_USER_HOME</c>.
	/// </param>
	/// <param name="additionalArgs">Optional extra arguments to pass to the emulator command line.</param>
	/// <returns>
	/// An <see cref="EmulatorLaunchResult"/> with the running process and launch details.
	/// Await <see cref="EmulatorLaunchResult.PortsResolvedAsync"/> before reading
	/// <see cref="EmulatorLaunchResult.ConsolePort"/> / <see cref="EmulatorLaunchResult.Serial"/>
	/// when ports were not pre-assigned.
	/// </returns>
	public EmulatorLaunchResult LaunchEmulator (
		string avdName,
		bool coldBoot = false,
		int? consolePort = null,
		int? adbPort = null,
		string? logFile = null,
		List<string>? additionalArgs = null)
	{
		if (string.IsNullOrWhiteSpace (avdName))
			throw new ArgumentException ("AVD name must not be empty.", nameof (avdName));

		var args = new List<string> { "-avd", avdName };
		if (coldBoot)
			args.Add ("-no-snapshot-load");

		// Pre-assign ports when requested; the serial is then known before the process starts.
		int? resolvedConsolePort = consolePort;
		int? resolvedAdbPort = adbPort;
		bool portsPreAssigned = consolePort.HasValue;

		if (consolePort.HasValue) {
			resolvedAdbPort ??= consolePort.Value + 1;
			args.Add ("-ports");
			args.Add ($"{consolePort.Value},{resolvedAdbPort.Value}");
		}

		// Resolve log path: use explicit override or compute from env vars.
		string resolvedLogPath;
		if (logFile is { Length: > 0 } nonEmptyLogFile) {
			resolvedLogPath = nonEmptyLogFile;
			args.Add ("-logfile");
			args.Add (nonEmptyLogFile);
		} else {
			resolvedLogPath = ResolveAvdLogPath (avdName);
		}

		if (additionalArgs != null)
			args.AddRange (additionalArgs);

		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, args.ToArray ());

		if (environmentVariables != null) {
			foreach (var kvp in environmentVariables)
				psi.EnvironmentVariables[kvp.Key] = kvp.Value;
		}

		// Redirect stdout/stderr so the emulator process doesn't inherit the
		// caller's pipes. Without this, parent processes (e.g. VS Code spawn)
		// never see the 'close' event because the emulator holds the pipes open.
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		logger.Invoke (TraceLevel.Verbose, $"Launching emulator AVD '{avdName}'");

		var process = new Process { StartInfo = psi };

		// When ports are not pre-assigned, parse stdout/stderr for the well-known boot lines
		// that report the assigned ports. A TaskCompletionSource signals callers once both
		// ports have been observed.
		TaskCompletionSource<bool>? tcs = portsPreAssigned
			? null
			: new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);

		var result = new EmulatorLaunchResult {
			Process = process,
			ConsolePort = resolvedConsolePort,
			AdbPort = resolvedAdbPort,
			LogPath = resolvedLogPath,
			PortsResolvedAsync = tcs is { } activeTcs ? (Task)activeTcs.Task : Task.CompletedTask,
		};

		process.OutputDataReceived += (_, e) => {
			if (e.Data == null)
				return;
			logger.Invoke (TraceLevel.Verbose, $"[emulator] {e.Data}");
			if (tcs != null)
				TryResolvePortsFromLine (e.Data, result, tcs);
		};
		process.ErrorDataReceived += (_, e) => {
			if (e.Data == null)
				return;
			logger.Invoke (TraceLevel.Warning, $"[emulator] {e.Data}");
			if (tcs != null)
				TryResolvePortsFromLine (e.Data, result, tcs);
		};

		if (tcs != null) {
			// If the process exits before the port lines are emitted, fault the task
			// so callers don't wait forever.
			process.EnableRaisingEvents = true;
			process.Exited += (_, _) => {
				int exitCode;
				try { exitCode = process.ExitCode; } catch { exitCode = -1; }
				tcs.TrySetException (new InvalidOperationException (
					$"Emulator process exited (code {exitCode}) before port assignment lines were emitted."));
			};
		}

		process.Start ();

		// Drain redirected streams asynchronously to prevent pipe buffer deadlocks.
		process.BeginOutputReadLine ();
		process.BeginErrorReadLine ();

		return result;
	}

	/// <summary>
	/// Parses a single emulator output line and, when the relevant port-assignment patterns are
	/// found, updates <paramref name="result"/> and completes <paramref name="tcs"/>.
	/// </summary>
	/// <remarks>
	/// The emulator emits (on stdout or stderr):
	///   <c>emulator: Listening on port NNNN</c>  (console port)
	///   <c>emulator: ADB Server has started successfully on port NNNN</c>  (adb port)
	/// These lines have been stable across emulator releases for years.
	/// </remarks>
	internal static void TryResolvePortsFromLine (string line, EmulatorLaunchResult result, TaskCompletionSource<bool> tcs)
	{
		const string listeningPrefix = "emulator: Listening on port ";
		const string adbPrefix = "emulator: ADB Server has started successfully on port ";

		if (line.StartsWith (listeningPrefix, StringComparison.Ordinal)) {
			if (int.TryParse (line.Substring (listeningPrefix.Length).Trim (), out var port))
				result.ConsolePort = port;
		} else if (line.StartsWith (adbPrefix, StringComparison.Ordinal)) {
			if (int.TryParse (line.Substring (adbPrefix.Length).Trim (), out var port))
				result.AdbPort = port;
		}

		if (result.ConsolePort.HasValue && result.AdbPort.HasValue)
			tcs.TrySetResult (true);
	}

	/// <summary>
	/// Resolves the default emulator log path for the given AVD name, respecting the
	/// <c>ANDROID_AVD_HOME</c> and <c>ANDROID_USER_HOME</c> environment variables
	/// (including any overrides set on this <see cref="EmulatorRunner"/> instance).
	/// Falls back to the AOSP convention: <c>~/.android/avd/&lt;name&gt;.avd/emulator.log</c>.
	/// </summary>
	internal string ResolveAvdLogPath (string avdName)
	{
		var avdDirName = avdName + ".avd";

		var avdHome = GetEffectiveEnvVar (EnvironmentVariableNames.AndroidAvdHome);
		if (!string.IsNullOrEmpty (avdHome))
			return Path.Combine (avdHome, avdDirName, "emulator.log");

		var userHome = GetEffectiveEnvVar (EnvironmentVariableNames.AndroidUserHome);
		if (!string.IsNullOrEmpty (userHome))
			return Path.Combine (userHome, "avd", avdDirName, "emulator.log");

		var home = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
		return Path.Combine (home, ".android", "avd", avdDirName, "emulator.log");
	}

	string? GetEffectiveEnvVar (string name)
	{
		if (environmentVariables != null && environmentVariables.TryGetValue (name, out var val))
			return val;
		return Environment.GetEnvironmentVariable (name);
	}

	public async Task<IReadOnlyList<string>> ListAvdNamesAsync (CancellationToken cancellationToken = default)
	{
		using var stdout = new StringWriter ();
		using var stderr = new StringWriter ();
		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, "-list-avds");

		logger.Invoke (TraceLevel.Verbose, "Running: emulator -list-avds");
		var exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, environmentVariables).ConfigureAwait (false);
		ProcessUtils.ThrowIfFailed (exitCode, "emulator -list-avds", stderr);

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
	/// Boots an emulator for the specified AVD and waits until it is fully ready to accept commands.
	/// <para>
	/// Unlike <see cref="LaunchEmulator"/>, which only spawns the emulator process, this method
	/// handles the full lifecycle: it checks whether the device is already online, launches
	/// the emulator if needed, then polls <c>sys.boot_completed</c> and <c>pm path android</c>
	/// until the Android OS is fully booted and the package manager is responsive.
	/// </para>
	/// <para>Ported from the dotnet/android <c>BootAndroidEmulator</c> MSBuild task.</para>
	/// </summary>
	/// <param name="deviceOrAvdName">
	/// Either an ADB device serial (e.g., <c>emulator-5554</c>) to wait for,
	/// or an AVD name (e.g., <c>Pixel_7_API_35</c>) to launch and boot.
	/// </param>
	/// <param name="adbRunner">An <see cref="AdbRunner"/> used to query device status and boot properties.</param>
	/// <param name="options">Optional boot configuration (timeout, poll interval, cold boot, extra args).</param>
	/// <param name="cancellationToken">Cancellation token to abort the operation.</param>
	/// <returns>
	/// An <see cref="EmulatorBootResult"/> indicating success or failure, including the device serial on success
	/// or an error message on timeout/failure.
	/// </returns>
	public async Task<EmulatorBootResult> BootEmulatorAsync (
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

		logger.Invoke (TraceLevel.Info, $"Booting emulator for '{deviceOrAvdName}'...");

		// Phase 1: Check if deviceOrAvdName is already an online ADB device by serial
		var devices = await adbRunner.ListDevicesAsync (cancellationToken).ConfigureAwait (false);
		var onlineDevice = devices.FirstOrDefault (d =>
			d.Status == AdbDeviceStatus.Online &&
			string.Equals (d.Serial, deviceOrAvdName, StringComparison.OrdinalIgnoreCase));

		if (onlineDevice != null) {
			logger.Invoke (TraceLevel.Info, $"Device '{deviceOrAvdName}' is already online.");
			return new EmulatorBootResult { Success = true, Serial = onlineDevice.Serial, ErrorKind = EmulatorBootErrorKind.None };
		}

		// Single timeout CTS for the entire boot operation (covers Phase 2 and Phase 3).
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeoutCts.CancelAfter (options.BootTimeout);

		// Phase 2: Check if AVD is already running (possibly still booting)
		var runningSerial = FindRunningAvdSerial (devices, deviceOrAvdName);
		if (runningSerial != null) {
			logger.Invoke (TraceLevel.Info, $"AVD '{deviceOrAvdName}' is already running as '{runningSerial}', waiting for full boot...");
			try {
				return await WaitForFullBootAsync (adbRunner, runningSerial, options, timeoutCts.Token).ConfigureAwait (false);
			} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
				return new EmulatorBootResult {
					Success = false,
					ErrorKind = EmulatorBootErrorKind.Timeout,
					ErrorMessage = $"Timed out waiting for emulator '{deviceOrAvdName}' to boot within {options.BootTimeout.TotalSeconds}s.",
				};
			}
		}

		// Phase 3: Launch the emulator
		logger.Invoke (TraceLevel.Info, $"Launching AVD '{deviceOrAvdName}'...");
		EmulatorLaunchResult launchResult;
		Process emulatorProcess;
		try {
			launchResult = LaunchEmulator (deviceOrAvdName, options.ColdBoot, additionalArgs: options.AdditionalArgs);
			emulatorProcess = launchResult.Process;
		} catch (Exception ex) {
			return new EmulatorBootResult {
				Success = false,
				ErrorKind = EmulatorBootErrorKind.LaunchFailed,
				ErrorMessage = $"Failed to launch emulator: {ex.Message}",
			};
		}

		// Poll for the new emulator serial to appear.
		// If the boot times out or is cancelled, terminate the process we spawned
		// to avoid leaving orphan emulator processes.
		//
		// On macOS, the emulator binary may fork the real QEMU process and exit with
		// code 0 immediately. The real emulator continues as a separate process and
		// will eventually appear in 'adb devices'. We only treat non-zero exit codes
		// as immediate failures; exit code 0 means we continue polling.
		try {
			string? newSerial = null;
			bool processExitedWithZero = false;
			while (newSerial == null) {
				timeoutCts.Token.ThrowIfCancellationRequested ();

				// Detect early process exit for fast failure
				if (emulatorProcess.HasExited && !processExitedWithZero) {
					if (emulatorProcess.ExitCode != 0) {
						emulatorProcess.Dispose ();
						return new EmulatorBootResult {
							Success = false,
							ErrorKind = EmulatorBootErrorKind.LaunchFailed,
							ErrorMessage = $"Emulator process for '{deviceOrAvdName}' exited with code {emulatorProcess.ExitCode} before becoming available.",
						};
					}
					// Exit code 0: emulator likely forked (common on macOS).
					// The real emulator runs as a separate process — keep polling.
					logger.Invoke (TraceLevel.Verbose, $"Emulator launcher process exited with code 0 (likely forked). Continuing to poll adb devices.");
					processExitedWithZero = true;
				}

				await Task.Delay (options.PollInterval, timeoutCts.Token).ConfigureAwait (false);

				devices = await adbRunner.ListDevicesAsync (timeoutCts.Token).ConfigureAwait (false);
				newSerial = FindRunningAvdSerial (devices, deviceOrAvdName);
			}

			logger.Invoke (TraceLevel.Info, $"Emulator appeared as '{newSerial}', waiting for full boot...");
			var result = await WaitForFullBootAsync (adbRunner, newSerial, options, timeoutCts.Token).ConfigureAwait (false);

			// Release the Process handle — the emulator process itself keeps running.
			// We no longer need stdout/stderr forwarding since boot is complete.
			emulatorProcess.Dispose ();
			return result;
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			TryKillProcess (emulatorProcess);
			return new EmulatorBootResult {
				Success = false,
				ErrorKind = EmulatorBootErrorKind.Timeout,
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

	void TryKillProcess (Process process)
	{
		try {
			process.Kill ();
		} catch (Exception ex) {
			// Best-effort: process may have already exited
			logger.Invoke (TraceLevel.Verbose, $"Failed to stop emulator process: {ex.Message}");
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
		// The caller is responsible for enforcing the overall boot timeout via
		// cancellationToken (a linked CTS with CancelAfter). This method simply
		// polls until boot completes or the token is cancelled.
		while (!cancellationToken.IsCancellationRequested) {
			cancellationToken.ThrowIfCancellationRequested ();

			var bootCompleted = await adbRunner.GetShellPropertyAsync (serial, "sys.boot_completed", cancellationToken).ConfigureAwait (false);
			if (string.Equals (bootCompleted, "1", StringComparison.Ordinal)) {
				var pmResult = await adbRunner.RunShellCommandAsync (serial, "pm path android", cancellationToken).ConfigureAwait (false);
				if (pmResult != null && pmResult.StartsWith ("package:", StringComparison.Ordinal)) {
					logger.Invoke (TraceLevel.Info, $"Emulator '{serial}' is fully booted.");
					return new EmulatorBootResult { Success = true, Serial = serial, ErrorKind = EmulatorBootErrorKind.None };
				}
			}

			await Task.Delay (options.PollInterval, cancellationToken).ConfigureAwait (false);
		}

		cancellationToken.ThrowIfCancellationRequested ();
		return new EmulatorBootResult { Success = false, ErrorKind = EmulatorBootErrorKind.Cancelled, ErrorMessage = "Boot cancelled." };
	}
}

