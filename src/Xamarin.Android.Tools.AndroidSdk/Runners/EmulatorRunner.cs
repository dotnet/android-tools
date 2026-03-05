// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

			var ext = OS.IsWindows ? ".exe" : "";
			var path = Path.Combine (sdkPath, "emulator", "emulator" + ext);

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

	public Process StartAvd (string avdName, bool coldBoot = false, IEnumerable<string>? additionalArgs = null)
	{
		var emulatorPath = RequireEmulatorPath ();

		var args = new List<string> { "-avd", avdName };
		if (coldBoot)
			args.Add ("-no-snapshot-load");
		if (additionalArgs != null)
			args.AddRange (additionalArgs);

		var psi = ProcessUtils.CreateProcessStartInfo (emulatorPath, args.ToArray ());
		ConfigureEnvironment (psi);

		// Don't redirect stdout/stderr for this long-running background process.
		// UseShellExecute=false (set by CreateProcessStartInfo) already prevents
		// pipe inheritance without needing redirect+drain.
		psi.RedirectStandardOutput = false;
		psi.RedirectStandardError = false;

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
}

