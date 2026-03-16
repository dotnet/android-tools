// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools;

/// <summary>
/// Represents a single ADB reverse port forwarding rule as reported by 'adb reverse --list'.
/// Uses positional record for value equality and built-in ToString().
/// </summary>
/// <param name="Remote">The remote (device-side) socket spec, e.g. "tcp:5000".</param>
/// <param name="Local">The local (host-side) socket spec, e.g. "tcp:5000".</param>
public record AdbReversePortRule (string Remote, string Local);
