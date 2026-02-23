using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Represents a manifest feed that provides information about downloadable SDK and JDK packages.
	/// Supports both Xamarin Android Manifest Feed and Google SDK Repository formats.
	/// </summary>
	public class ManifestFeed
	{
		static readonly HttpClient httpClient = new HttpClient ();

		/// <summary>
		/// Gets the list of packages available in this manifest feed.
		/// </summary>
		public List<PackageInfo> Packages { get; } = new List<PackageInfo> ();

		/// <summary>
		/// Gets the feed URL this manifest was loaded from.
		/// </summary>
		public string? FeedUrl { get; private set; }

		Action<TraceLevel, string> logger;

		public ManifestFeed (Action<TraceLevel, string>? logger = null)
		{
			this.logger = logger ?? AndroidSdkInfo.DefaultConsoleLogger;
		}

		/// <summary>
		/// Downloads and parses a manifest feed from the specified URL.
		/// </summary>
		/// <param name="feedUrl">The URL of the manifest feed (XML or JSON).</param>
		/// <param name="logger">Optional logger for diagnostic messages.</param>
		/// <param name="cancellationToken">Optional cancellation token.</param>
		/// <returns>A ManifestFeed instance with parsed package information.</returns>
		public static async Task<ManifestFeed> LoadAsync (string feedUrl, Action<TraceLevel, string>? logger = null, CancellationToken cancellationToken = default)
		{
			logger = logger ?? AndroidSdkInfo.DefaultConsoleLogger;
			logger (TraceLevel.Info, $"Loading manifest feed from: {feedUrl}");

			var manifest = new ManifestFeed (logger);
			manifest.FeedUrl = feedUrl;

			try {
				var response = await httpClient.GetAsync (feedUrl, cancellationToken).ConfigureAwait (false);
				response.EnsureSuccessStatusCode ();

				var content = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);

				// Detect format based on content
				if (content.TrimStart ().StartsWith ("<", StringComparison.Ordinal)) {
					// XML format - Google SDK Repository
					GoogleRepositoryParser.Parse (content, manifest, logger);
				} else if (content.TrimStart ().StartsWith ("{", StringComparison.Ordinal)) {
					// JSON format - Xamarin Android Manifest Feed
					XamarinManifestParser.Parse (content, manifest, logger);
				} else {
					throw new FormatException ("Unknown manifest feed format. Expected XML or JSON.");
				}

				logger (TraceLevel.Info, $"Successfully loaded {manifest.Packages.Count} packages from manifest feed.");
			} catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to load manifest feed: {ex.Message}");
				throw;
			}

			return manifest;
		}

		/// <summary>
		/// Loads a manifest feed from a local file (for offline/CI scenarios).
		/// </summary>
		/// <param name="filePath">The path to the local manifest file.</param>
		/// <param name="logger">Optional logger for diagnostic messages.</param>
		/// <returns>A ManifestFeed instance with parsed package information.</returns>
		public static ManifestFeed LoadFromFile (string filePath, Action<TraceLevel, string>? logger = null)
		{
			logger = logger ?? AndroidSdkInfo.DefaultConsoleLogger;
			logger (TraceLevel.Info, $"Loading manifest feed from file: {filePath}");

			var manifest = new ManifestFeed (logger);
			manifest.FeedUrl = filePath;

			try {
				var content = File.ReadAllText (filePath);

				// Detect format based on content
				if (content.TrimStart ().StartsWith ("<", StringComparison.Ordinal)) {
					// XML format - Google SDK Repository
					GoogleRepositoryParser.Parse (content, manifest, logger);
				} else if (content.TrimStart ().StartsWith ("{", StringComparison.Ordinal)) {
					// JSON format - Xamarin Android Manifest Feed
					XamarinManifestParser.Parse (content, manifest, logger);
				} else {
					throw new FormatException ("Unknown manifest feed format. Expected XML or JSON.");
				}

				logger (TraceLevel.Info, $"Successfully loaded {manifest.Packages.Count} packages from local file.");
			} catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to load manifest feed from file: {ex.Message}");
				throw;
			}

			return manifest;
		}

		/// <summary>
		/// Caches the manifest feed to a local file for offline use.
		/// </summary>
		/// <param name="localPath">The path where the manifest should be cached.</param>
		public async Task CacheAsync (string localPath, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty (FeedUrl))
				throw new InvalidOperationException ("Cannot cache a manifest that was not loaded from a URL.");

			logger (TraceLevel.Info, $"Caching manifest feed to: {localPath}");

			try {
				var directory = Path.GetDirectoryName (localPath);
				if (!string.IsNullOrEmpty (directory) && !Directory.Exists (directory)) {
					Directory.CreateDirectory (directory);
				}

				var response = await httpClient.GetAsync (FeedUrl, cancellationToken).ConfigureAwait (false);
				response.EnsureSuccessStatusCode ();

				var content = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
				File.WriteAllText (localPath, content);

				logger (TraceLevel.Info, "Manifest feed cached successfully.");
			} catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to cache manifest feed: {ex.Message}");
				throw;
			}
		}

		/// <summary>
		/// Resolves a specific package by its path identifier.
		/// </summary>
		/// <param name="packagePath">The package path (e.g., "cmdline-tools;20.0", "platform-tools").</param>
		/// <param name="os">The operating system (e.g., "linux", "macosx", "windows"). If null, returns package without filtering.</param>
		/// <param name="arch">The architecture (e.g., "x86_64", "aarch64"). If null, returns first matching OS.</param>
		/// <returns>The resolved package with archive information, or null if not found.</returns>
		public PackageInfo? ResolvePackage (string packagePath, string? os = null, string? arch = null)
		{
			var package = Packages.FirstOrDefault (p => string.Equals (p.Path, packagePath, StringComparison.OrdinalIgnoreCase));
			if (package == null) {
				logger (TraceLevel.Warning, $"Package not found: {packagePath}");
				return null;
			}

			// If no OS specified, return package as-is
			if (string.IsNullOrEmpty (os))
				return package;

			// Filter archives by OS and optionally by architecture
			var filteredPackage = new PackageInfo {
				Path = package.Path,
				DisplayName = package.DisplayName,
				Version = package.Version,
				License = package.License,
				Description = package.Description,
				PackageType = package.PackageType,
				Obsolete = package.Obsolete
			};

			var normalizedOs = NormalizeOsName (os);

			foreach (var archive in package.Archives) {
				var archiveOs = NormalizeOsName (archive.HostOs);
				if (string.Equals (archiveOs, normalizedOs, StringComparison.OrdinalIgnoreCase)) {
					if (string.IsNullOrEmpty (arch) || string.Equals (archive.Arch, arch, StringComparison.OrdinalIgnoreCase)) {
						filteredPackage.Archives.Add (archive);
					}
				}
			}

			if (filteredPackage.Archives.Count == 0) {
				logger (TraceLevel.Warning, $"No archive found for package {packagePath} on {os}/{arch}");
				return null;
			}

			return filteredPackage;
		}

		/// <summary>
		/// Gets all available JDK versions from the manifest.
		/// </summary>
		/// <returns>List of JDK packages with version information.</returns>
		public List<PackageInfo> GetJdkVersions ()
		{
			return Packages
				.Where (p => p.Path != null && (
					p.Path.StartsWith ("jdk", StringComparison.OrdinalIgnoreCase) ||
					p.Path.IndexOf ("java", StringComparison.OrdinalIgnoreCase) >= 0 ||
					p.Path.IndexOf ("openjdk", StringComparison.OrdinalIgnoreCase) >= 0))
				.ToList ();
		}

		/// <summary>
		/// Gets all available JDK versions from the manifest with vendor information.
		/// </summary>
		/// <returns>List of JDK packages with vendor details.</returns>
		public List<JdkPackageInfo> GetJdkPackages ()
		{
			return Packages
				.Where (p => p.Path != null && (
					p.Path.IndexOf ("jdk", StringComparison.OrdinalIgnoreCase) >= 0 ||
					p.Path.IndexOf ("java", StringComparison.OrdinalIgnoreCase) >= 0 ||
					p.Path.IndexOf ("openjdk", StringComparison.OrdinalIgnoreCase) >= 0))
				.Select (p => {
					var jdkPackage = JdkPackageInfo.FromPackageInfo (p);
					jdkPackage.ParseVendorFromPath ();
					return jdkPackage;
				})
				.ToList ();
		}

		/// <summary>
		/// Gets JDK packages filtered by vendor.
		/// </summary>
		/// <param name="vendorId">The vendor identifier (e.g., "microsoft", "adoptium", "azul").</param>
		/// <returns>List of JDK packages from the specified vendor.</returns>
		public List<JdkPackageInfo> GetJdkPackagesByVendor (string vendorId)
		{
			return GetJdkPackages ()
				.Where (p => string.Equals (p.VendorId, vendorId, StringComparison.OrdinalIgnoreCase))
				.ToList ();
		}

		/// <summary>
		/// Gets JDK packages filtered by major version.
		/// </summary>
		/// <param name="majorVersion">The major JDK version (e.g., 17, 21).</param>
		/// <returns>List of JDK packages with the specified major version.</returns>
		public List<JdkPackageInfo> GetJdkPackagesByMajorVersion (int majorVersion)
		{
			return GetJdkPackages ()
				.Where (p => p.GetMajorVersion () == majorVersion)
				.ToList ();
		}

		/// <summary>
		/// Verifies that a downloaded file matches the expected SHA-1 checksum.
		/// </summary>
		/// <param name="filePath">Path to the downloaded file.</param>
		/// <param name="expectedSha1">Expected SHA-1 checksum in hexadecimal format.</param>
		/// <returns>True if the checksum matches, false otherwise.</returns>
		public static bool VerifyChecksum (string filePath, string expectedSha1)
		{
			if (string.IsNullOrEmpty (expectedSha1))
				return false;

			try {
				using (var sha1 = SHA1.Create ())
				using (var stream = File.OpenRead (filePath)) {
					var hashBytes = sha1.ComputeHash (stream);
					var actualSha1 = BitConverter.ToString (hashBytes).Replace ("-", "").ToLowerInvariant ();
					return string.Equals (actualSha1, expectedSha1.ToLowerInvariant (), StringComparison.Ordinal);
				}
			} catch {
				return false;
			}
		}

		static string? NormalizeOsName (string? os)
		{
			if (string.IsNullOrEmpty (os))
				return os;

			// Normalize OS names to match manifest conventions
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
	}
}
