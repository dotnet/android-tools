// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Information about a connected Android device.
	/// </summary>
	public class AdbDeviceInfo
	{
		/// <summary>
		/// Gets or sets the device serial number.
		/// </summary>
		public string Serial { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the device state (e.g., "device", "offline").
		/// </summary>
		public string? State { get; set; }

		/// <summary>
		/// Gets or sets the device model name.
		/// </summary>
		public string? Model { get; set; }

		/// <summary>
		/// Gets or sets the device code name.
		/// </summary>
		public string? Device { get; set; }

		/// <summary>
		/// Gets whether this device is an emulator.
		/// </summary>
		public bool IsEmulator => Serial.StartsWith ("emulator-");
	}
}
