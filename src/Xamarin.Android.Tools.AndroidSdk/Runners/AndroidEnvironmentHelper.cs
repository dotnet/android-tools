// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;

namespace Xamarin.Android.Tools;

/// <summary>
/// Helper for setting up environment variables for Android SDK tools.
/// </summary>
internal static class AndroidEnvironmentHelper
{
	/// <summary>
	/// Configures environment variables on a ProcessStartInfo for running Android SDK tools.
	/// </summary>
	internal static void ConfigureEnvironment (ProcessStartInfo psi, string? sdkPath, string? jdkPath)
	{
		if (!string.IsNullOrEmpty (sdkPath))
			psi.EnvironmentVariables [EnvironmentVariableNames.AndroidHome] = sdkPath;

		if (!string.IsNullOrEmpty (jdkPath)) {
			psi.EnvironmentVariables [EnvironmentVariableNames.JavaHome] = jdkPath;
			var jdkBin = Path.Combine (jdkPath!, "bin");
			var currentPath = psi.EnvironmentVariables [EnvironmentVariableNames.Path] ?? "";
			psi.EnvironmentVariables [EnvironmentVariableNames.Path] = string.IsNullOrEmpty (currentPath) ? jdkBin : jdkBin + Path.PathSeparator + currentPath;
		}

		// Set ANDROID_USER_HOME for consistent AVD location across tools (matches SdkManager behavior)
		if (!psi.EnvironmentVariables.ContainsKey (EnvironmentVariableNames.AndroidUserHome)) {
			psi.EnvironmentVariables [EnvironmentVariableNames.AndroidUserHome] = Path.Combine (
				Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".android");
		}
	}
}
