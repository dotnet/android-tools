// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Provides utility methods for Android environment configuration, ABI mapping,
	/// and API level information.
	/// </summary>
	public static class AndroidEnvironmentHelper
	{
		static readonly Dictionary<string, string> AbiToArchMap = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
			["armeabi-v7a"] = "arm",
			["arm64-v8a"] = "aarch64",
			["x86"] = "x86",
			["x86_64"] = "x86_64",
		};

		static readonly Dictionary<string, string> ApiLevelToVersionMap = new Dictionary<string, string> {
			["21"] = "5.0",
			["22"] = "5.1",
			["23"] = "6.0",
			["24"] = "7.0",
			["25"] = "7.1",
			["26"] = "8.0",
			["27"] = "8.1",
			["28"] = "9.0",
			["29"] = "10.0",
			["30"] = "11.0",
			["31"] = "12.0",
			["32"] = "12.1",
			["33"] = "13.0",
			["34"] = "14.0",
			["35"] = "15.0",
			["36"] = "16.0",
		};

		/// <summary>
		/// Builds a dictionary of environment variables for Android SDK tool processes.
		/// </summary>
		/// <param name="sdkPath">The Android SDK path. Sets ANDROID_HOME when provided.</param>
		/// <param name="jdkPath">The JDK path. Sets JAVA_HOME when provided.</param>
		/// <returns>A dictionary of environment variables, or null if both paths are null.</returns>
		public static Dictionary<string, string>? GetEnvironment (string? sdkPath, string? jdkPath)
		{
			var env = new Dictionary<string, string> ();
			if (!string.IsNullOrEmpty (sdkPath))
				env [EnvironmentVariableNames.AndroidHome] = sdkPath!;
			if (!string.IsNullOrEmpty (jdkPath))
				env [EnvironmentVariableNames.JavaHome] = jdkPath!;
			return env.Count > 0 ? env : null;
		}

		/// <summary>
		/// Maps an Android ABI (e.g., "arm64-v8a") to its CPU architecture name (e.g., "aarch64").
		/// </summary>
		/// <param name="abi">The Android ABI string.</param>
		/// <returns>The architecture name, or null if the ABI is not recognized.</returns>
		public static string? MapAbiToArchitecture (string? abi)
		{
			if (abi != null && AbiToArchMap.TryGetValue (abi, out var arch))
				return arch;
			return null;
		}

		/// <summary>
		/// Gets .NET runtime identifiers for a given CPU architecture.
		/// </summary>
		/// <param name="architecture">The CPU architecture (e.g., "x86_64", "aarch64").</param>
		/// <returns>An array of runtime identifiers, or null if not recognized.</returns>
		public static string[]? GetRuntimeIdentifiers (string? architecture)
		{
			return architecture switch {
				"x86_64" or "amd64" => new [] { "android-x64" },
				"aarch64" or "arm64" => new [] { "android-arm64" },
				"x86" => new [] { "android-x86" },
				"arm" => new [] { "android-arm" },
				_ => null,
			};
		}

		/// <summary>
		/// Maps an Android API level to its version string (e.g., "35" → "15.0").
		/// </summary>
		/// <param name="apiLevel">The API level as a string.</param>
		/// <returns>The version string, or null if not recognized.</returns>
		public static string? MapApiLevelToVersion (string? apiLevel)
		{
			if (apiLevel != null && ApiLevelToVersionMap.TryGetValue (apiLevel, out var version))
				return version;
			return null;
		}

		/// <summary>
		/// Maps a system image tag ID to a human-readable display name.
		/// </summary>
		/// <param name="tagId">The tag ID (e.g., "google_apis", "google_apis_playstore").</param>
		/// <param name="playStoreEnabled">Whether Play Store is enabled for the image.</param>
		/// <returns>A display name for the tag.</returns>
		public static string? MapTagIdToDisplayName (string? tagId, bool playStoreEnabled = false)
		{
			return tagId switch {
				"google_apis" => playStoreEnabled ? "Google Play" : "Google APIs",
				"google_apis_playstore" => "Google Play",
				"android-wear" => "Wear OS",
				"android-tv" => "Android TV",
				"android-automotive" => "Android Automotive",
				"default" => "Default",
				_ => tagId,
			};
		}
	}
}
