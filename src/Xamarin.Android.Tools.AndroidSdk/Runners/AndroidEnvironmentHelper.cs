// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;

namespace Xamarin.Android.Tools
{
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
env ["ANDROID_HOME"] = sdkPath;

if (!string.IsNullOrEmpty (jdkPath)) {
env ["JAVA_HOME"] = jdkPath;
var jdkBin = Path.Combine (jdkPath, "bin");
var currentPath = Environment.GetEnvironmentVariable ("PATH") ?? "";
env ["PATH"] = string.IsNullOrEmpty (currentPath) ? jdkBin : jdkBin + Path.PathSeparator + currentPath;
}

return env;
}
}
}
