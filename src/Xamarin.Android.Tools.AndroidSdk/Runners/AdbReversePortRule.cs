// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools;

/// <summary>
/// Represents a single ADB reverse port forwarding rule as reported by 'adb reverse --list'.
/// </summary>
public class AdbReversePortRule
{
	/// <summary>
	/// The remote (device-side) socket spec, e.g. "tcp:5000".
	/// </summary>
	public string Remote { get; init; } = string.Empty;

	/// <summary>
	/// The local (host-side) socket spec, e.g. "tcp:5000".
	/// </summary>
	public string Local { get; init; } = string.Empty;
}
