// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Result of an emulator boot operation.
	/// </summary>
	public class EmulatorBootResult
	{
		public bool Success { get; set; }
		public string? Serial { get; set; }
		public string? ErrorMessage { get; set; }
	}
}
