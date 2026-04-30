// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

/// <summary>
/// Returned by <see cref="EmulatorRunner.LaunchEmulator"/> with enriched launch information.
/// </summary>
public sealed class EmulatorLaunchResult
{
	public EmulatorLaunchResult (Process process, string logPath)
	{
		Process = process;
		LogPath = logPath;
	}

	/// <summary>The running emulator process.</summary>
	public Process Process { get; }

	/// <summary>The OS process ID of the emulator process.</summary>
	public int Pid => Process.Id;

	/// <summary>
	/// The emulator console port (e.g., 5554). Populated either from the pre-assigned
	/// <c>-ports</c> argument or once <see cref="PortsResolvedAsync"/> completes.
	/// </summary>
	public int? ConsolePort { get; internal set; }

	/// <summary>
	/// The emulator ADB port (e.g., 5555). Populated either from the pre-assigned
	/// <c>-ports</c> argument or once <see cref="PortsResolvedAsync"/> completes.
	/// </summary>
	public int? AdbPort { get; internal set; }

	/// <summary>
	/// The ADB serial for this emulator (e.g., <c>emulator-5554</c>), derived from <see cref="ConsolePort"/>.
	/// Returns <c>null</c> until <see cref="ConsolePort"/> is populated.
	/// </summary>
	public string? Serial => ConsolePort is int p ? $"emulator-{p}" : null;

	/// <summary>
	/// The path to the emulator log file. Resolved at launch time from the <c>-logfile</c>
	/// argument (if specified) or from the <c>ANDROID_AVD_HOME</c> / <c>ANDROID_USER_HOME</c>
	/// environment variables, falling back to the AOSP default
	/// (<c>~/.android/avd/&lt;name&gt;.avd/emulator.log</c>).
	/// </summary>
	public string LogPath { get; }

	/// <summary>
	/// A <see cref="Task"/> that completes when the emulator has reported its console and ADB
	/// port assignments via stdout/stderr. If ports were pre-assigned via <c>-ports</c>, this
	/// task is already completed. Await this before reading <see cref="ConsolePort"/>,
	/// <see cref="AdbPort"/>, or <see cref="Serial"/> when ports were not pre-assigned.
	/// The task faults with <see cref="System.InvalidOperationException"/> if the emulator
	/// process exits before the port lines are emitted.
	/// </summary>
	public Task PortsResolvedAsync { get; init; } = Task.CompletedTask;
}
