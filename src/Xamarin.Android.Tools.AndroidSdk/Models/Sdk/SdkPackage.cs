// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Information about an SDK package as reported by the <c>sdkmanager</c> CLI.
	/// </summary>
	public class SdkPackage
	{
		/// <summary>Package path (e.g. "platform-tools", "platforms;android-35").</summary>
		public string Path { get; set; } = "";

		/// <summary>Installed or available version.</summary>
		public string? Version { get; set; }

		/// <summary>Human-readable description.</summary>
		public string? Description { get; set; }

		/// <summary>Whether this package is currently installed.</summary>
		public bool IsInstalled { get; set; }
	}
}

