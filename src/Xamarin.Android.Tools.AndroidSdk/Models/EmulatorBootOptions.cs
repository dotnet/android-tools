// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Options for booting an Android emulator.
	/// </summary>
	public class EmulatorBootOptions
	{
		public TimeSpan BootTimeout { get; set; } = TimeSpan.FromSeconds (300);
		public string? AdditionalArgs { get; set; }
		public bool ColdBoot { get; set; }
		public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds (500);
	}
}
