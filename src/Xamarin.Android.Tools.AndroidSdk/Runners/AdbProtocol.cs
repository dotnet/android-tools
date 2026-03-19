// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools;

/// <summary>
/// Protocol types supported by adb port forwarding and reverse port forwarding.
/// </summary>
public enum AdbProtocol
{
	Tcp,
	LocalAbstract,
	LocalReserved,
	LocalFilesystem,
}
