// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Provides utility methods for Android environment configuration.
	/// </summary>
	public static class AndroidEnvironmentHelper
	{
		static readonly Dictionary<string, string> AbiToArchMap = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
			["armeabi-v7a"] = "arm",
			["arm64-v8a"] = "aarch64",
			["x86"] = "x86",
			["x86_64"] = "x86_64",
		};

		/// <summary>
		/// Builds a dictionary of environment variables for Android SDK tool processes.
		/// </summary>
		public static Dictionary<string, string>? GetEnvironment (string? sdkPath, string? jdkPath)
		{
			var env = new Dictionary<string, string> ();
			if (!string.IsNullOrEmpty (sdkPath))
				env ["ANDROID_HOME"] = sdkPath!;
			if (!string.IsNullOrEmpty (jdkPath))
				env ["JAVA_HOME"] = jdkPath!;
			return env.Count > 0 ? env : null;
		}

		/// <summary>
		/// Maps an Android ABI (e.g., "arm64-v8a") to its CPU architecture name (e.g., "aarch64").
		/// </summary>
		public static string? MapAbiToArchitecture (string? abi)
		{
			if (abi != null && AbiToArchMap.TryGetValue (abi, out var arch))
				return arch;
			return null;
		}
	}
}
