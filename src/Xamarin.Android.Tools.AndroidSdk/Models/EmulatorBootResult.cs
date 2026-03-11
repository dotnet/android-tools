// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools;

/// <summary>
/// Result of an emulator boot operation.
/// </summary>
public record EmulatorBootResult
{
	public bool Success { get; init; }
	public string? Serial { get; init; }
	public string? ErrorMessage { get; init; }
}
