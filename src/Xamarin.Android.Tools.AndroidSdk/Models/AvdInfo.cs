// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
/// <summary>
/// Information about an Android Virtual Device (AVD).
/// </summary>
public class AvdInfo
{
/// <summary>
/// Gets or sets the AVD name.
/// </summary>
public string Name { get; set; } = string.Empty;

/// <summary>
/// Gets or sets the device profile (e.g., "Pixel 6").
/// </summary>
public string? DeviceProfile { get; set; }

/// <summary>
/// Gets or sets the path to the AVD directory.
/// </summary>
public string? Path { get; set; }
}
}
