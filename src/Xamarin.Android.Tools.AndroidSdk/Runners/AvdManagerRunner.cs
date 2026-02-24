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
/// Runs Android Virtual Device Manager (avdmanager) commands.
/// </summary>
public class AvdManagerRunner
{
readonly Func<string?> getSdkPath;

/// <summary>
/// Creates a new <see cref="AvdManagerRunner"/>.
/// </summary>
/// <param name="getSdkPath">Function that returns the Android SDK path.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="getSdkPath"/> is null.</exception>
public AvdManagerRunner (Func<string?> getSdkPath)
{
this.getSdkPath = getSdkPath ?? throw new ArgumentNullException (nameof (getSdkPath));
}

/// <summary>
/// Gets the path to the avdmanager executable, or null if not found.
/// </summary>
public string? AvdManagerPath {
get {
var sdkPath = getSdkPath ();
if (string.IsNullOrEmpty (sdkPath))
return null;

var ext = OS.IsWindows ? ".bat" : "";
var cmdlineToolsPath = Path.Combine (sdkPath, "cmdline-tools", "latest", "bin", "avdmanager" + ext);
if (File.Exists (cmdlineToolsPath))
return cmdlineToolsPath;

var toolsPath = Path.Combine (sdkPath, "tools", "bin", "avdmanager" + ext);

return File.Exists (toolsPath) ? toolsPath : null;
}
}

/// <summary>
/// Gets whether the AVD Manager is available.
/// </summary>
public bool IsAvailable => !string.IsNullOrEmpty (AvdManagerPath);

/// <summary>
/// Lists all configured AVDs.
/// </summary>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A list of configured AVDs.</returns>
/// <exception cref="InvalidOperationException">Thrown when AVD Manager is not found.</exception>
public async Task<List<AvdInfo>> ListAvdsAsync (CancellationToken cancellationToken = default)
{
if (!IsAvailable)
throw new InvalidOperationException ("AVD Manager not found.");

var stdout = new StringWriter ();
var psi = new ProcessStartInfo {
FileName = AvdManagerPath!,
Arguments = "list avd",
UseShellExecute = false,
CreateNoWindow = true
};
await ProcessUtils.StartProcess (psi, stdout, null, cancellationToken).ConfigureAwait (false);

var avds = new List<AvdInfo> ();
string? currentName = null, currentDevice = null, currentPath = null;

foreach (var line in stdout.ToString ().Split ('\n')) {
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
/// Deletes an AVD.
/// </summary>
/// <param name="name">The name of the AVD to delete.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <exception cref="InvalidOperationException">Thrown when AVD Manager is not found.</exception>
public async Task DeleteAvdAsync (string name, CancellationToken cancellationToken = default)
{
if (!IsAvailable)
throw new InvalidOperationException ("AVD Manager not found.");

var psi = new ProcessStartInfo {
FileName = AvdManagerPath!,
Arguments = $"delete avd --name \"{name}\"",
UseShellExecute = false,
CreateNoWindow = true
};
await ProcessUtils.StartProcess (psi, null, null, cancellationToken).ConfigureAwait (false);
}
}
}
