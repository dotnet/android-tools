// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	public class AdbDeviceInfo
	{
		public string Serial { get; set; } = string.Empty;
		public string? State { get; set; }
		public string? Model { get; set; }
		public string? Device { get; set; }
		public bool IsEmulator => Serial.StartsWith ("emulator-");
	}
}
