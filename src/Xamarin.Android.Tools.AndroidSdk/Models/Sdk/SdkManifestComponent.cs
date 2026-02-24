// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Information about a component available in the SDK manifest feed.
	/// Named SdkManifestComponent to avoid confusion with AndroidManifest.xml.
	/// </summary>
	public class SdkManifestComponent
	{
		/// <summary>Element name in the manifest (e.g. "cmdline-tools", "platform-tools").</summary>
		public string ElementName { get; set; } = "";

		/// <summary>Component version/revision.</summary>
		public string Revision { get; set; } = "";

		/// <summary>SDK-style path (e.g. "cmdline-tools;19.0").</summary>
		public string? Path { get; set; }

		/// <summary>Filesystem destination path (e.g. "cmdline-tools/latest").</summary>
		public string? FilesystemPath { get; set; }

		/// <summary>Human-readable description.</summary>
		public string? Description { get; set; }

		/// <summary>Download URL for the current platform.</summary>
		public string? DownloadUrl { get; set; }

		/// <summary>Expected file size in bytes.</summary>
		public long Size { get; set; }

		/// <summary>Checksum value (typically SHA-1).</summary>
		public string? Checksum { get; set; }

		/// <summary>Checksum algorithm (e.g. "sha1").</summary>
		public string? ChecksumType { get; set; }

		/// <summary>Whether this component is marked obsolete.</summary>
		public bool IsObsolete { get; set; }
	}
}

