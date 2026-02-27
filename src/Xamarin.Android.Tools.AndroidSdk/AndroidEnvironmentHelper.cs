// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Provides utility methods for Android environment configuration.
	/// </summary>
	public static class AndroidEnvironmentHelper
	{
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
	}
}
