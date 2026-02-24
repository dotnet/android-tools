// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Progress information for SDK bootstrap operations.
	/// </summary>
	public class SdkBootstrapProgress
	{
		/// <summary>Current phase of the bootstrap operation.</summary>
		public SdkBootstrapPhase Phase { get; set; }

		/// <summary>Download progress percentage (0-100), or -1 if unknown.</summary>
		public int PercentComplete { get; set; } = -1;

		/// <summary>Human-readable status message.</summary>
		public string Message { get; set; } = "";
	}
}

