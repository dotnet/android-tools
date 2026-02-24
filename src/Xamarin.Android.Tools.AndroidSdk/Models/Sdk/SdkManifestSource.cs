// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Specifies the manifest feed source for SDK component discovery.
	/// </summary>
	public enum SdkManifestSource
	{
		/// <summary>Use Xamarin/Microsoft manifest feed (default).</summary>
		Xamarin,
		/// <summary>Use Google's official Android SDK repository manifest.</summary>
		Google
	}
}

