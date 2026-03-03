// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Xamarin.Android.Tools;

/// <summary>
/// Helper for setting up environment variables for Android SDK tools.
/// </summary>
public static class AndroidEnvironmentHelper
{
	/// <summary>
	/// Creates environment variables for running Android SDK tools.
	/// </summary>
	public static Dictionary<string, string>? GetToolEnvironment (string? sdkPath, string? jdkPath)
	{
		if (string.IsNullOrEmpty (sdkPath) && string.IsNullOrEmpty (jdkPath))
			return null;

		var env = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);

		if (!string.IsNullOrEmpty (sdkPath))
			env [EnvironmentVariableNames.AndroidHome] = sdkPath;

		if (!string.IsNullOrEmpty (jdkPath)) {
			env [EnvironmentVariableNames.JavaHome] = jdkPath;
			var jdkBin = Path.Combine (jdkPath, "bin");
			var currentPath = Environment.GetEnvironmentVariable (EnvironmentVariableNames.Path) ?? "";
			env [EnvironmentVariableNames.Path] = string.IsNullOrEmpty (currentPath) ? jdkBin : jdkBin + Path.PathSeparator + currentPath;
		}

		return env;
	}

	/// <summary>
	/// Configures environment variables on a ProcessStartInfo for running Android SDK tools.
	/// </summary>
	public static void ConfigureEnvironment (ProcessStartInfo psi, string? sdkPath, string? jdkPath)
	{
		if (!string.IsNullOrEmpty (sdkPath))
			psi.EnvironmentVariables [EnvironmentVariableNames.AndroidHome] = sdkPath;

		if (!string.IsNullOrEmpty (jdkPath)) {
			psi.EnvironmentVariables [EnvironmentVariableNames.JavaHome] = jdkPath;
			var jdkBin = Path.Combine (jdkPath, "bin");
			var currentPath = psi.EnvironmentVariables [EnvironmentVariableNames.Path] ?? "";
			psi.EnvironmentVariables [EnvironmentVariableNames.Path] = string.IsNullOrEmpty (currentPath) ? jdkBin : jdkBin + Path.PathSeparator + currentPath;
		}
	}
}
