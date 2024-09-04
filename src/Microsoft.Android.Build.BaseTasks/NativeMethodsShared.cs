// From: https://github.com/dotnet/msbuild/blob/7cf66090a764f0f239671e4877255efe7ba91155/src/Framework/NativeMethods.cs#L788
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Build.Utilities;

static class NativeMethodsShared
{
	private static bool? _isWindows;

	/// <summary>
	/// Gets a flag indicating if we are running under some version of Windows
	/// </summary>
	internal static bool IsWindows {
		get {
			_isWindows ??= RuntimeInformation.IsOSPlatform (OSPlatform.Windows);
			return _isWindows.Value;
		}
	}
}
