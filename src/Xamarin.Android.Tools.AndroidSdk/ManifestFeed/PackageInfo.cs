using System;
using System.Collections.Generic;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Represents information about a downloadable package from a manifest feed.
	/// </summary>
	public class PackageInfo
	{
		/// <summary>
		/// Gets or sets the unique package identifier (e.g., "cmdline-tools;20.0", "platform-tools", "jdk-17").
		/// </summary>
		public string Path { get; set; } = "";

		/// <summary>
		/// Gets or sets the display name of the package.
		/// </summary>
		public string? DisplayName { get; set; }

		/// <summary>
		/// Gets or sets the package version.
		/// </summary>
		public Version? Version { get; set; }

		/// <summary>
		/// Gets or sets the list of archives available for different platforms and architectures.
		/// </summary>
		public List<ArchiveInfo> Archives { get; set; } = new List<ArchiveInfo> ();

		/// <summary>
		/// Gets or sets the license identifier required for this package.
		/// </summary>
		public string? License { get; set; }

		/// <summary>
		/// Gets or sets package description.
		/// </summary>
		public string? Description { get; set; }
	}

	/// <summary>
	/// Represents a downloadable archive for a specific platform and architecture.
	/// </summary>
	public class ArchiveInfo
	{
		/// <summary>
		/// Gets or sets the download URL for the archive.
		/// </summary>
		public string Url { get; set; } = "";

		/// <summary>
		/// Gets or sets the SHA-1 checksum of the archive.
		/// </summary>
		public string Sha1 { get; set; } = "";

		/// <summary>
		/// Gets or sets the size of the archive in bytes.
		/// </summary>
		public long Size { get; set; }

		/// <summary>
		/// Gets or sets the host operating system for this archive (e.g., "linux", "macosx", "windows").
		/// </summary>
		public string? HostOs { get; set; }

		/// <summary>
		/// Gets or sets the architecture for this archive (e.g., "x86_64", "aarch64").
		/// </summary>
		public string? Arch { get; set; }
	}
}
