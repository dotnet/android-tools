// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Represents an Android SDK license that may need to be accepted.
	/// </summary>
	public class SdkLicense
	{
		/// <summary>
		/// Gets or sets the license identifier (e.g., "android-sdk-license", "android-sdk-preview-license").
		/// </summary>
		public string Id { get; set; } = "";

		/// <summary>
		/// Gets or sets the full license text that should be presented to the user.
		/// </summary>
		public string Text { get; set; } = "";
	}
}
