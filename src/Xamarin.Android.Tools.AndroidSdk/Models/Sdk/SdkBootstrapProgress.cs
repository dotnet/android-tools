// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Progress information for SDK bootstrap operations.
	/// </summary>
	public record SdkBootstrapProgress
	{
		public SdkBootstrapPhase Phase { get; set; }
		public int PercentComplete { get; set; } = -1;
		public string Message { get; set; } = "";
	}
}

