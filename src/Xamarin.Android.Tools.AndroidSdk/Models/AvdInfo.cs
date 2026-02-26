// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	public class AvdInfo
	{
		public string Name { get; set; } = string.Empty;
		public string? DeviceProfile { get; set; }
		public string? Path { get; set; }
	}
}
