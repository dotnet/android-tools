using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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

		/// <summary>
		/// Gets or sets the package type (e.g., "cmdline-tools", "platform-tools", "platforms", "build-tools").
		/// </summary>
		public string? PackageType { get; set; }

		/// <summary>
		/// Gets or sets whether this package is obsolete.
		/// </summary>
		public bool Obsolete { get; set; }

		/// <summary>
		/// Gets the best archive for the current system based on OS and architecture.
		/// </summary>
		/// <returns>The best matching archive, or null if none found.</returns>
		public ArchiveInfo? GetBestArchiveForCurrentSystem ()
		{
			if (Archives.Count == 0)
				return null;

			var currentOs = GetCurrentOsName ();
			var currentArch = GetCurrentArchName ();

			// First try exact match (OS + arch)
			var exactMatch = Archives.FirstOrDefault (a =>
				string.Equals (NormalizeOsName (a.HostOs), currentOs, StringComparison.OrdinalIgnoreCase) &&
				string.Equals (NormalizeArchName (a.Arch), currentArch, StringComparison.OrdinalIgnoreCase));

			if (exactMatch != null)
				return exactMatch;

			// Then try OS-only match
			var osMatch = Archives.FirstOrDefault (a =>
				string.Equals (NormalizeOsName (a.HostOs), currentOs, StringComparison.OrdinalIgnoreCase));

			if (osMatch != null)
				return osMatch;

			// Return first archive if no match (may be cross-platform)
			return Archives.FirstOrDefault (a => string.IsNullOrEmpty (a.HostOs));
		}

		static string GetCurrentOsName ()
		{
			if (OS.IsWindows)
				return "windows";
			if (OS.IsMac)
				return "macosx";
			return "linux";
		}

		static string GetCurrentArchName ()
		{
			var arch = RuntimeInformation.OSArchitecture;
			switch (arch) {
				case Architecture.X64:
					return "x86_64";
				case Architecture.Arm64:
					return "aarch64";
				case Architecture.X86:
					return "x86";
				case Architecture.Arm:
					return "arm";
				default:
					return "x86_64";
			}
		}

		static string? NormalizeOsName (string? os)
		{
			if (string.IsNullOrEmpty (os))
				return os;

			switch (os.ToLowerInvariant ()) {
				case "windows":
				case "win":
					return "windows";
				case "macosx":
				case "macos":
				case "darwin":
				case "osx":
					return "macosx";
				case "linux":
					return "linux";
				default:
					return os.ToLowerInvariant ();
			}
		}

		static string? NormalizeArchName (string? arch)
		{
			if (string.IsNullOrEmpty (arch))
				return arch;

			switch (arch.ToLowerInvariant ()) {
				case "x86_64":
				case "x64":
				case "amd64":
					return "x86_64";
				case "aarch64":
				case "arm64":
					return "aarch64";
				case "x86":
				case "i386":
				case "i686":
					return "x86";
				default:
					return arch.ToLowerInvariant ();
			}
		}
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
		/// Gets or sets the checksum of the archive.
		/// </summary>
		public string Checksum { get; set; } = "";

		/// <summary>
		/// Gets or sets the type of checksum (e.g., "sha1", "sha256"). Defaults to "sha1".
		/// </summary>
		public string ChecksumType { get; set; } = "sha1";

		/// <summary>
		/// Gets or sets the SHA-1 checksum of the archive. Alias for Checksum when ChecksumType is "sha1".
		/// </summary>
		public string Sha1 {
			get => string.Equals (ChecksumType, "sha1", StringComparison.OrdinalIgnoreCase) ? Checksum : "";
			set {
				Checksum = value;
				ChecksumType = "sha1";
			}
		}

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

		/// <summary>
		/// Gets or sets the host bits (32 or 64).
		/// </summary>
		public uint HostBits { get; set; }

		/// <summary>
		/// Determines if this archive is valid for the current system.
		/// </summary>
		/// <returns>True if the archive matches the current OS and architecture.</returns>
		public bool IsValidForCurrentSystem ()
		{
			// If no HostOs specified, assume cross-platform
			if (string.IsNullOrEmpty (HostOs))
				return true;

			var currentOs = GetCurrentOsName ();
			if (!string.Equals (NormalizeOsName (HostOs), currentOs, StringComparison.OrdinalIgnoreCase))
				return false;

			// If no Arch specified, assume any architecture matches
			if (string.IsNullOrEmpty (Arch))
				return true;

			var currentArch = GetCurrentArchName ();
			return string.Equals (NormalizeArchName (Arch), currentArch, StringComparison.OrdinalIgnoreCase);
		}

		static string GetCurrentOsName ()
		{
			if (OS.IsWindows)
				return "windows";
			if (OS.IsMac)
				return "macosx";
			return "linux";
		}

		static string GetCurrentArchName ()
		{
			var arch = RuntimeInformation.OSArchitecture;
			switch (arch) {
				case Architecture.X64:
					return "x86_64";
				case Architecture.Arm64:
					return "aarch64";
				case Architecture.X86:
					return "x86";
				case Architecture.Arm:
					return "arm";
				default:
					return "x86_64";
			}
		}

		static string? NormalizeOsName (string? os)
		{
			if (string.IsNullOrEmpty (os))
				return os;

			switch (os.ToLowerInvariant ()) {
				case "windows":
				case "win":
					return "windows";
				case "macosx":
				case "macos":
				case "darwin":
				case "osx":
					return "macosx";
				case "linux":
					return "linux";
				default:
					return os.ToLowerInvariant ();
			}
		}

		static string? NormalizeArchName (string? arch)
		{
			if (string.IsNullOrEmpty (arch))
				return arch;

			switch (arch.ToLowerInvariant ()) {
				case "x86_64":
				case "x64":
				case "amd64":
					return "x86_64";
				case "aarch64":
				case "arm64":
					return "aarch64";
				case "x86":
				case "i386":
				case "i686":
					return "x86";
				default:
					return arch.ToLowerInvariant ();
			}
		}
	}

	/// <summary>
	/// Represents JDK-specific package information with vendor details.
	/// </summary>
	public class JdkPackageInfo : PackageInfo
	{
		/// <summary>
		/// Gets or sets the vendor identifier (e.g., "microsoft", "adoptium", "azul").
		/// </summary>
		public string? VendorId { get; set; }

		/// <summary>
		/// Gets or sets the vendor display name (e.g., "Microsoft", "Eclipse Adoptium", "Azul").
		/// </summary>
		public string? VendorDisplay { get; set; }

		/// <summary>
		/// Gets or sets whether this is a preview/early access release.
		/// </summary>
		public bool Preview { get; set; }

		/// <summary>
		/// Gets the major version of the JDK (e.g., 17 from version 17.0.1).
		/// </summary>
		/// <returns>The major version number, or 0 if version is not available.</returns>
		public int GetMajorVersion ()
		{
			if (Version == null)
				return 0;

			return Version.Major;
		}

		/// <summary>
		/// Creates a JdkPackageInfo from a base PackageInfo.
		/// </summary>
		public static JdkPackageInfo FromPackageInfo (PackageInfo package)
		{
			return new JdkPackageInfo {
				Path = package.Path,
				DisplayName = package.DisplayName,
				Version = package.Version,
				Archives = package.Archives,
				License = package.License,
				Description = package.Description,
				PackageType = package.PackageType,
				Obsolete = package.Obsolete
			};
		}

		/// <summary>
		/// Parses vendor information from the package path or display name.
		/// </summary>
		public void ParseVendorFromPath ()
		{
			if (string.IsNullOrEmpty (Path) && string.IsNullOrEmpty (DisplayName))
				return;

			var source = Path ?? DisplayName ?? "";
			var sourceLower = source.ToLowerInvariant ();

			if (sourceLower.IndexOf ("microsoft", StringComparison.Ordinal) >= 0) {
				VendorId = "microsoft";
				VendorDisplay = "Microsoft";
			} else if (sourceLower.IndexOf ("adoptium", StringComparison.Ordinal) >= 0 ||
			           sourceLower.IndexOf ("temurin", StringComparison.Ordinal) >= 0) {
				VendorId = "adoptium";
				VendorDisplay = "Eclipse Adoptium";
			} else if (sourceLower.IndexOf ("azul", StringComparison.Ordinal) >= 0 ||
			           sourceLower.IndexOf ("zulu", StringComparison.Ordinal) >= 0) {
				VendorId = "azul";
				VendorDisplay = "Azul";
			} else if (sourceLower.IndexOf ("oracle", StringComparison.Ordinal) >= 0) {
				VendorId = "oracle";
				VendorDisplay = "Oracle";
			} else if (sourceLower.IndexOf ("amazon", StringComparison.Ordinal) >= 0 ||
			           sourceLower.IndexOf ("corretto", StringComparison.Ordinal) >= 0) {
				VendorId = "amazon";
				VendorDisplay = "Amazon Corretto";
			}

			// Check for preview indicators
			Preview = sourceLower.IndexOf ("preview", StringComparison.Ordinal) >= 0 ||
			          sourceLower.IndexOf ("ea", StringComparison.Ordinal) >= 0 ||
			          sourceLower.IndexOf ("early-access", StringComparison.Ordinal) >= 0;
		}
	}
}
