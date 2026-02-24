// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
/// <summary>
/// Runs Android Emulator commands.
/// </summary>
public class EmulatorRunner
{
readonly Func<string?> getSdkPath;

public EmulatorRunner (Func<string?> getSdkPath)
{
this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
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

public bool IsAvailable => EmulatorPath != null;

/// <summary>
/// Starts an AVD and returns the process.
/// </summary>
public Process StartAvd (string avdName, bool coldBoot = false, string? additionalArgs = null)
{
if (!IsAvailable)
throw new InvalidOperationException ("Android Emulator not found.");

var args = $"-avd \"{avdName}\"";
if (coldBoot)
args += " -no-snapshot-load";
if (!string.IsNullOrEmpty (additionalArgs))
args += " " + additionalArgs;

var psi = new ProcessStartInfo {
FileName = EmulatorPath!,
Arguments = args,
UseShellExecute = false,
CreateNoWindow = true
};

var process = new Process { StartInfo = psi };
process.Start ();
return process;
}

/// <summary>
/// Lists the names of installed AVDs.
/// </summary>
public async Task<List<string>> ListAvdNamesAsync (CancellationToken cancellationToken = default)
{
if (!IsAvailable)
throw new InvalidOperationException ("Android Emulator not found.");

var stdout = new StringWriter ();
var psi = new ProcessStartInfo {
FileName = EmulatorPath!,
Arguments = "-list-avds",
UseShellExecute = false,
CreateNoWindow = true
};

var exitCode = await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

var avds = new List<string> ();
foreach (var line in stdout.ToString ().Split ('\n')) {
var trimmed = line.Trim ();
if (!string.IsNullOrEmpty (trimmed))
avds.Add (trimmed);
}
return avds;
}
}
}
