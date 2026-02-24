// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Constants for environment variable names used by Android SDK tooling.
	/// </summary>
	public static class EnvironmentVariableNames
	{
		/// <summary>
		/// The ANDROID_HOME environment variable specifying the Android SDK root directory.
		/// This is the preferred variable for modern Android tooling.
		/// </summary>
		public const string AndroidHome = "ANDROID_HOME";

		/// <summary>
		/// The ANDROID_SDK_ROOT environment variable specifying the Android SDK root directory.
		/// This is an older variable name, but still supported by many tools.
		/// </summary>
		public const string AndroidSdkRoot = "ANDROID_SDK_ROOT";

		/// <summary>
		/// The JAVA_HOME environment variable specifying the JDK installation directory.
		/// </summary>
		public const string JavaHome = "JAVA_HOME";

		/// <summary>
		/// The JI_JAVA_HOME environment variable for internal/override JDK path.
		/// Takes precedence over JAVA_HOME when set.
		/// </summary>
		public const string JiJavaHome = "JI_JAVA_HOME";

		/// <summary>
		/// The PATH environment variable for executable search paths.
		/// </summary>
		public const string Path = "PATH";

		/// <summary>
		/// The PATHEXT environment variable for executable file extensions (Windows).
		/// </summary>
		public const string PathExt = "PATHEXT";
	}
}
